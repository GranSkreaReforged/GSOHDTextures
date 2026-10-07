"""Export every Texture2D in the game to <work>/dump/<key>.png and describe them in <work>/index.json.

usage: dump.py <GSO_Data dir> <work dir> [--only SUBSTRING] [--min-size N]
"""
import argparse
import json
import os
import sys

import UnityPy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # run with -I, which drops the script dir
from common import texture_key

# Baked lighting and font atlases must keep their exact texels.
SKIP_PREFIXES = ('Lightmap-', 'ReflectionProbe-', 'Font Texture')


def asset_files(data):
    for f in sorted(os.listdir(data)):
        if f.endswith('.assets') or (f.startswith('level') and '.' not in f):
            yield f


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('data')
    ap.add_argument('work')
    ap.add_argument('--only', default='', help='only dump textures whose name contains this (case-insensitive)')
    ap.add_argument('--min-size', type=int, default=32, help='skip textures whose larger side is below this')
    args = ap.parse_args()

    dump_dir = os.path.join(args.work, 'dump')
    os.makedirs(dump_dir, exist_ok=True)
    # Merge into an existing index so an --only run doesn't forget the rest.
    index_path = os.path.join(args.work, 'index.json')
    index = {}
    if os.path.exists(index_path):
        with open(index_path, encoding='utf-8') as fp:
            index = {e['key']: e for e in json.load(fp)}
    this_run = set()
    written = 0

    for f in asset_files(args.data):
        env = UnityPy.load(os.path.join(args.data, f))
        for obj in env.objects:
            if obj.type.name != 'Texture2D':
                continue
            t = obj.read()
            name, w, h = t.m_Name, t.m_Width, t.m_Height
            if args.only and args.only.lower() not in name.lower():
                continue
            key = texture_key(name, w, h)
            if key in this_run:
                continue  # the same texture duplicated in another asset file
            this_run.add(key)
            entry = {'key': key, 'name': name, 'file': f, 'width': w, 'height': h,
                     'format': str(t.m_TextureFormat).split('.')[-1], 'mips': getattr(t, 'm_MipCount', 1)}
            index[key] = entry

            if name.startswith(SKIP_PREFIXES):
                entry['skip'] = 'lighting-or-font'
                continue
            if max(w, h) < args.min_size:
                entry['skip'] = 'too-small'
                continue

            out = os.path.join(dump_dir, key + '.png')
            if os.path.exists(out):
                continue
            try:
                im = t.image
            except Exception as e:  # unsupported/crunched formats
                entry['skip'] = f'decode-failed: {e}'
                continue
            if im.mode == 'RGBA' and im.getchannel('A').getextrema() == (255, 255):
                im = im.convert('RGB')
            im.save(out)
            written += 1
        print(f'{f}: {len(this_run)} textures so far', flush=True)

    entries = sorted(index.values(), key=lambda e: e['key'].lower())
    with open(index_path, 'w', encoding='utf-8') as fp:
        json.dump(entries, fp, indent=1)
    skipped = sum(1 for e in entries if 'skip' in e)
    print(f'{len(entries)} textures indexed, {skipped} skipped, {written} new PNGs in {dump_dir}')


if __name__ == '__main__':
    main()
