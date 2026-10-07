"""Build the final texture pack in <work>/pack from upscaled output, overrides and plain resizes.
Each texture is written as <key>.dds (BC7 with mipmaps, see dds.py) so the plugin loads it without stalling.

Source per texture, first match wins:
  1. <work>/overrides/<key>.png   hand-made or hand-fixed, used at its own size
  2. <work>/upscaled/<key>.png    AI output (wrap padding cropped off, resized to the target, tone-matched)
  3. normal and other data maps, and textures bigger than prep.py's --max-input: Lanczos resize of the original
Anything else (not upscaled yet) is left out, so the game keeps its original.

usage: pack.py <work dir> [--scale 2] [--max-size 4096] [--only-list FILE] [--max-input 2048] [--jobs 6]
"""
import argparse
import os
import sys
from concurrent.futures import ProcessPoolExecutor

from PIL import Image, ImageChops, ImageFilter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # run with -I, which drops the script dir
from common import is_data_map, load_index, wrap_pad
from dds import save_bc7

Image.MAX_IMAGE_PIXELS = None  # x4 output of a padded 2048 texture is 10240^2; these are our own files


def target_size(w, h, scale, max_size):
    f = min(scale, max_size / max(w, h))
    # DXT compression in the plugin needs multiples of 4.
    return max(4, round(w * f / 4) * 4), max(4, round(h * f / 4) * 4)


# Radius, in original pixels, of the colour/brightness correction. Small enough to keep the AI's
# detail, large enough not to bring back the original's compression noise.
TONE_BLUR = 1.5


def tone_match(rgb, original):
    """Correct the AI output's low frequencies so it averages back to the original. Real-ESRGAN drifts
    in brightness and colour (one grass splat came out 45% darker); the detail it adds is kept."""
    src = original.convert('RGB')
    blur = ImageFilter.GaussianBlur(TONE_BLUR)
    down = rgb.resize(src.size, Image.BOX)
    diff = ImageChops.subtract(src.filter(blur), down.filter(blur), 1, 128)
    return ImageChops.add(rgb, diff.resize(rgb.size, Image.BICUBIC), 1, -128)


def with_alpha(rgb, original, size):
    """Upscaled colour plus the original alpha, Lanczos-resized (AI upscalers mangle alpha)."""
    if original.mode not in ('RGBA', 'LA'):
        return rgb.convert('RGB')
    alpha = original.getchannel('A').resize(size, Image.LANCZOS)
    out = rgb.convert('RGB')
    out.putalpha(alpha)
    return out


def build(e, work, scale, max_size, max_input):
    key, w, h = e['key'], e['width'], e['height']
    size = target_size(w, h, scale, max_size)
    if size[0] <= w and size[1] <= h:
        return None, 'not-larger'

    override = os.path.join(work, 'overrides', key + '.png')
    if os.path.exists(override):
        with Image.open(override) as im:
            im.load()
            return im, 'override'

    dump = os.path.join(work, 'dump', key + '.png')
    if not os.path.exists(dump):
        return None, 'not-dumped'
    with Image.open(dump) as original:
        original.load()

    upscaled = os.path.join(work, 'upscaled', key + '.png')
    if os.path.exists(upscaled) and not is_data_map(e):  # ignore AI output left from before a rule change
        with Image.open(upscaled) as up:
            up.load()
        pad = wrap_pad(w, h)
        f = up.width / (w + 2 * pad)
        # Crop the padding off and resize in one step: a cropped copy of a 10240^2 output costs ~300 MB.
        rgb = up.resize(size, Image.LANCZOS, box=(pad * f, pad * f, (pad + w) * f, (pad + h) * f))
        del up
        return with_alpha(tone_match(rgb, original), original, size), 'upscaled'

    if is_data_map(e) or max(w, h) > max_input:
        return original.resize(size, Image.LANCZOS), 'resized'
    return None, 'pending'


def pack_one(e, out, args):
    """Build and write one texture; runs in a worker process."""
    im, how = build(e, args.work, args.scale, args.max_size, args.max_input)
    if im is not None:
        save_bc7(im, out + '.part')
        os.replace(out + '.part', out)  # a killed run never leaves a truncated file that looks up to date
    return how


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('work')
    ap.add_argument('--scale', type=float, default=2.0)
    ap.add_argument('--max-size', type=int, default=4096)
    ap.add_argument('--max-input', type=int, default=2048)
    ap.add_argument('--only-list', help='file of texture keys (one per line), e.g. BepInEx/GSOHDTextures-seen.txt')
    # Each worker can hold a 10240^2 AI output (~300 MB) plus copies, so this is bounded by RAM, not cores.
    ap.add_argument('--jobs', type=int, default=6)
    args = ap.parse_args()

    only = None
    if args.only_list:
        with open(args.only_list, encoding='utf-8') as f:
            only = {line.strip() for line in f if line.strip()}

    out_dir = os.path.join(args.work, 'pack')
    os.makedirs(out_dir, exist_ok=True)
    counts = {}
    jobs = {}
    wanted = set()
    with ProcessPoolExecutor(args.jobs) as pool:
        for e in load_index(args.work):
            key = e['key']
            if 'skip' in e or (only is not None and key not in only):
                continue
            out = os.path.join(out_dir, key + '.dds')
            sources = [os.path.join(args.work, d, key + '.png') for d in ('overrides', 'upscaled', 'dump')]
            newest = max((os.path.getmtime(s) for s in sources if os.path.exists(s)), default=0)
            if os.path.exists(out) and os.path.getmtime(out) >= newest:
                counts['unchanged'] = counts.get('unchanged', 0) + 1
                wanted.add(key + '.dds')
                continue
            jobs[key] = pool.submit(pack_one, e, out, args)

        for i, (key, job) in enumerate(jobs.items(), 1):
            how = job.result()
            counts[how] = counts.get(how, 0) + 1
            if how in ('override', 'upscaled', 'resized'):
                wanted.add(key + '.dds')
            if i % 250 == 0:
                print(f'  {i}/{len(jobs)}', flush=True)

    # Keep exactly what the current rules produce; this also clears PNGs from older versions of the pack.
    removed = 0
    for f in os.listdir(out_dir):
        if f.endswith(('.png', '.dds', '.part')) and f not in wanted:
            os.remove(os.path.join(out_dir, f))
            removed += 1

    print(f'{len(wanted)} textures in {out_dir} ({removed} stale removed)')
    for how, n in sorted(counts.items()):
        print(f'  {how}: {n}')


if __name__ == '__main__':
    main()
