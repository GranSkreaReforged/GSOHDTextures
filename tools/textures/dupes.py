"""Find texture keys shared by different images. The dump keeps the first texture per key, so a key
whose copies differ would replace some of them with the wrong art.

Prints each shared key with its asset files and whether the pixels match.

usage: dupes.py <GSO_Data dir>
"""
import argparse
import hashlib
import os
import sys
from collections import defaultdict

import UnityPy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # run with -I, which drops the script dir
from common import texture_key
from dump import asset_files


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('data')
    args = ap.parse_args()

    copies = defaultdict(list)  # lower-case key -> [(file, path_id, key)]
    for f in asset_files(args.data):
        env = UnityPy.load(os.path.join(args.data, f))
        for obj in env.objects:
            if obj.type.name == 'Texture2D':
                name = obj.peek_name() if hasattr(obj, 'peek_name') else obj.read().m_Name
                copies[name.lower()].append((f, obj.path_id))

    # Names alone are cheap; only names seen more than once are decoded and compared.
    shared = {n: c for n, c in copies.items() if len(c) > 1}
    by_file = defaultdict(list)
    for n, c in shared.items():
        for f, pid in c:
            by_file[f].append(pid)

    digests = defaultdict(list)  # key.lower() -> [(file, key, digest)]
    for f, pids in by_file.items():
        env = UnityPy.load(os.path.join(args.data, f))
        wanted = set(pids)
        for obj in env.objects:
            if obj.path_id not in wanted or obj.type.name != 'Texture2D':
                continue
            t = obj.read()
            key = texture_key(t.m_Name, t.m_Width, t.m_Height)
            try:
                digest = hashlib.sha1(t.image.tobytes()).hexdigest()[:12]
            except Exception as e:
                digest = f'undecodable ({e})'
            digests[key.lower()].append((f, key, digest))

    differing = 0
    for k, entries in sorted(digests.items()):
        if len(entries) < 2:
            continue
        same = len({d for _, _, d in entries}) == 1
        differing += not same
        print(('same ' if same else 'DIFF ') + entries[0][1])
        if not same:
            for f, key, d in entries:
                print(f'       {d}  {f}  {key}')
    print(f'{differing} shared keys have differing images')


if __name__ == '__main__':
    main()
