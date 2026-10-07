"""Shared helpers for the texture pipeline. Keep texture_key() identical to TextureKey.cs."""
import json
import os
import re

FORBIDDEN = set('<>:"/\\|?*')

# Normal and height maps go through a plain Lanczos resize: AI upscalers invent detail that breaks lighting.
NORMAL_MAP = re.compile(r'(?i)(normal|_nrm|_nm$|_n$|bump|height)')


def sanitize(name):
    return ''.join('_' if ord(c) < 32 or c in FORBIDDEN else c for c in name).strip()


def texture_key(name, width, height):
    return f'{sanitize(name)}__{width}x{height}'


def wrap_pad(width, height):
    """Pixels of wrapped border added around a texture before upscaling, so tiling edges stay seamless."""
    return max(4, min(width, height) // 8)


def load_index(work):
    with open(os.path.join(work, 'index.json'), encoding='utf-8') as f:
        return json.load(f)


def is_normal_map(entry):
    return bool(NORMAL_MAP.search(entry['name']))
