"""Stage dumped textures that still need AI upscaling into <work>/upscale_in.

Each one is RGB only (pack.py restores alpha) and wrap-padded by wrap_pad() pixels on every side,
so the upscaler sees the opposite edge and tiling textures stay seamless.

usage: prep.py <work dir> [--max-input N] [--only-list FILE]
"""
import argparse
import os
import sys
import shutil

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # run with -I, which drops the script dir
from common import is_data_map, load_index, wrap_pad

Image.MAX_IMAGE_PIXELS = None  # x4 output of a padded 2048 texture is 10240^2; these are our own files


def padded(im, pad):
    w, h = im.size
    out = Image.new(im.mode, (w + 2 * pad, h + 2 * pad))
    for dx in (-w, 0, w):
        for dy in (-h, 0, h):
            out.paste(im, (pad + dx, pad + dy))
    return out


def drop_truncated(done_dir, newest=8):
    """An interrupted upscale can leave its last few outputs half-written, and resuming skips any
    file that exists. Fully decode the newest outputs and delete broken ones so they are redone."""
    if not os.path.isdir(done_dir):
        return
    paths = sorted((os.path.join(done_dir, f) for f in os.listdir(done_dir)), key=os.path.getmtime)
    for path in paths[-newest:]:
        try:
            with Image.open(path) as im:
                im.load()
        except Exception as e:
            print(f'Removing truncated {os.path.basename(path)} ({e})')
            os.remove(path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('work')
    ap.add_argument('--max-input', type=int, default=2048, help='leave textures larger than this to a plain resize')
    ap.add_argument('--only-list', help='file of texture keys (one per line) to limit the batch to')
    args = ap.parse_args()

    only = None
    if args.only_list:
        with open(args.only_list, encoding='utf-8') as f:
            only = {line.strip() for line in f if line.strip()}

    stage = os.path.join(args.work, 'upscale_in')
    if os.path.isdir(stage):
        shutil.rmtree(stage)
    os.makedirs(stage)
    done_dir = os.path.join(args.work, 'upscaled')
    drop_truncated(done_dir)

    staged = 0
    for e in load_index(args.work):
        key = e['key']
        if 'skip' in e or is_data_map(e) or max(e['width'], e['height']) > args.max_input:
            continue
        if only is not None and key not in only:
            continue
        if os.path.exists(os.path.join(done_dir, key + '.png')):
            continue
        src = os.path.join(args.work, 'dump', key + '.png')
        if not os.path.exists(src):
            continue
        with Image.open(src) as im:
            padded(im.convert('RGB'), wrap_pad(*im.size)).save(os.path.join(stage, key + '.png'))
        staged += 1
    print(f'{staged} textures staged for upscaling in {stage}')


if __name__ == '__main__':
    main()
