"""Build the final texture pack in <work>/pack from upscaled output, overrides and plain resizes.

Source per texture, first match wins:
  1. <work>/overrides/<key>.png   hand-made or hand-fixed, used at its own size
  2. <work>/upscaled/<key>.png    AI output (wrap padding cropped off, resized to the target)
  3. normal maps, and textures bigger than prep.py's --max-input: Lanczos resize of the original
Anything else (not upscaled yet) is left out, so the game keeps its original.

usage: pack.py <work dir> [--scale 2] [--max-size 4096] [--only-list FILE] [--max-input 2048]
"""
import argparse
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # run with -I, which drops the script dir
from common import is_normal_map, load_index, wrap_pad


def target_size(w, h, scale, max_size):
    f = min(scale, max_size / max(w, h))
    # DXT compression in the plugin needs multiples of 4.
    return max(4, round(w * f / 4) * 4), max(4, round(h * f / 4) * 4)


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
    if os.path.exists(upscaled):
        with Image.open(upscaled) as up:
            up.load()
        pad = wrap_pad(w, h)
        f = up.width / (w + 2 * pad)
        rgb = up.crop((round(pad * f), round(pad * f), round((pad + w) * f), round((pad + h) * f)))
        return with_alpha(rgb.resize(size, Image.LANCZOS), original, size), 'upscaled'

    if is_normal_map(e) or max(w, h) > max_input:
        return original.resize(size, Image.LANCZOS), 'resized'
    return None, 'pending'


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('work')
    ap.add_argument('--scale', type=float, default=2.0)
    ap.add_argument('--max-size', type=int, default=4096)
    ap.add_argument('--max-input', type=int, default=2048)
    ap.add_argument('--only-list', help='file of texture keys (one per line), e.g. BepInEx/GSOHDTextures-seen.txt')
    args = ap.parse_args()

    only = None
    if args.only_list:
        with open(args.only_list, encoding='utf-8') as f:
            only = {line.strip() for line in f if line.strip()}

    out_dir = os.path.join(args.work, 'pack')
    os.makedirs(out_dir, exist_ok=True)
    wanted = set()
    counts = {}

    for e in load_index(args.work):
        key = e['key']
        if 'skip' in e or (only is not None and key not in only):
            continue
        out = os.path.join(out_dir, key + '.png')
        sources = [os.path.join(args.work, d, key + '.png') for d in ('overrides', 'upscaled', 'dump')]
        newest = max((os.path.getmtime(s) for s in sources if os.path.exists(s)), default=0)
        if os.path.exists(out) and os.path.getmtime(out) >= newest:
            wanted.add(key + '.png')
            counts['unchanged'] = counts.get('unchanged', 0) + 1
            continue

        im, how = build(e, args.work, args.scale, args.max_size, args.max_input)
        counts[how] = counts.get(how, 0) + 1
        if im is None:
            continue
        im.save(out, optimize=False, compress_level=6)
        wanted.add(key + '.png')

    removed = 0
    for f in os.listdir(out_dir):
        if f.endswith('.png') and f not in wanted:
            os.remove(os.path.join(out_dir, f))
            removed += 1

    print(f'{len(wanted)} textures in {out_dir} ({removed} stale removed)')
    for how, n in sorted(counts.items()):
        print(f'  {how}: {n}')


if __name__ == '__main__':
    main()
