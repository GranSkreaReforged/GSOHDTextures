"""Shared helpers for the texture pipeline. Keep texture_key() identical to TextureKey.cs."""
import json
import os
import re

FORBIDDEN = set('<>:"/\\|?*')

# Non-colour data goes through a plain Lanczos resize: AI upscalers invent detail that breaks lighting,
# and treat masks and gloss/AO maps as noisy photos. Checked against all names in the game's index.
NORMAL_MAP = re.compile(r'(?i)(norm|nrm|nml|_nm$|_n$|_n[ _]|_nor$|bump|height|_disp)')
DATA_MAP = re.compile(r'(?i)(metallic|smoothness|gloss|specular|spec$|spec[ _]|occlusion|[ _-]ao$|[ _]ao[ _]|mask'
                      r'|_m$|(?<!alb)_s$|_s 1$|_r$|roughness|_metall$|[ _]met\d?$)|(?-i:AO$)')


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


def is_data_map(entry):
    """Normal, height, metallic, gloss, AO or mask map: resized, never AI-upscaled."""
    return bool(NORMAL_MAP.search(entry['name']) or DATA_MAP.search(entry['name']))
