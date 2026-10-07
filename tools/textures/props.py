"""List the shader texture properties the game's materials actually use, to keep the plugin's
TextureReplacer.BuiltInProperties complete (Unity 2017.4 can't enumerate them at runtime).

Prints each property with the number of materials that assign a texture to it, the shaders using it,
and whether it is covered by the plugin's built-in list.

usage: props.py <GSO_Data dir> <plugin source dir>
"""
import argparse
import os
import re
import sys
from collections import Counter, defaultdict

import UnityPy

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))  # run with -I, which drops the script dir
from dump import asset_files


def builtin_properties(src):
    with open(os.path.join(src, 'TextureReplacer.cs'), encoding='utf-8') as f:
        block = re.search(r'BuiltInProperties\s*=\s*\{(.*?)\};', f.read(), re.S).group(1)
    return {p.rstrip('!') for p in re.findall(r'"([^"]+)"', block)}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('data')
    ap.add_argument('src')
    args = ap.parse_args()

    builtin = builtin_properties(args.src)
    used = Counter()
    shaders = defaultdict(Counter)
    shader_names = {}  # parsing a shader is slow, and materials share a few hundred of them
    for f in asset_files(args.data):
        env = UnityPy.load(os.path.join(args.data, f))
        for obj in env.objects:
            if obj.type.name != 'Material':
                continue
            m = obj.read()
            ref = (f, m.m_Shader.m_FileID, m.m_Shader.m_PathID)
            if ref not in shader_names:
                try:
                    shader_names[ref] = m.m_Shader.read().m_ParsedForm.m_Name
                except Exception:
                    shader_names[ref] = '?'
            shader = shader_names[ref]
            for name, tex_env in m.m_SavedProperties.m_TexEnvs:
                if tex_env.m_Texture.path_id:
                    used[name] += 1
                    shaders[name][shader] += 1

    for name, n in used.most_common():
        mark = 'ok ' if name in builtin else 'NEW'
        top = ', '.join(f'{s} ({c})' for s, c in shaders[name].most_common(3))
        print(f'{mark} {name:28} {n:6}  {top}')


if __name__ == '__main__':
    main()
