"""Write textures as BC7 DDS files with a full mip chain, ready for Texture2D.LoadRawTextureData.

The plugin reads these without decoding or compressing anything, so a scene loads its HD textures
as fast as the disk allows. BC7 is the same size as DXT5 and much closer to the source.
The layout is a standard DDS with a DX10 header, so any DDS viewer opens the files.
"""
import struct

import etcpak
from PIL import Image

DXGI_FORMAT_BC7_UNORM = 98
HEADER_SIZE = 4 + 124 + 20  # magic, DDS_HEADER, DDS_HEADER_DXT10; the plugin reads the same offsets


def mip_chain(im):
    """Levels down to 1x1, each a box filter of the one above (what Unity's own mipmaps use)."""
    levels = [im]
    while im.width > 1 or im.height > 1:
        im = im.resize((max(1, im.width // 2), max(1, im.height // 2)), Image.BOX)
        levels.append(im)
    return levels


def block_aligned(im):
    """BC7 works on 4x4 blocks. Levels smaller than that are tiled to fill one block."""
    w, h = im.size
    if w % 4 == 0 and h % 4 == 0:
        return im
    out = Image.new(im.mode, ((w + 3) // 4 * 4, (h + 3) // 4 * 4))
    for x in range(0, out.width, w):
        for y in range(0, out.height, h):
            out.paste(im, (x, y))
    return out


def header(width, height, mips, level0_size):
    flags = 0x1 | 0x2 | 0x4 | 0x1000 | 0x20000 | 0x80000  # CAPS HEIGHT WIDTH PIXELFORMAT MIPMAPCOUNT LINEARSIZE
    pixel_format = struct.pack('<II4s5I', 32, 0x4, b'DX10', 0, 0, 0, 0, 0)  # DDPF_FOURCC
    caps = 0x1000 | (0x400000 | 0x8 if mips > 1 else 0)  # TEXTURE, MIPMAP | COMPLEX
    dds_header = struct.pack('<7I44x', 124, flags, height, width, level0_size, 0, mips) \
        + pixel_format + struct.pack('<4I4x', caps, 0, 0, 0)
    dx10 = struct.pack('<5I', DXGI_FORMAT_BC7_UNORM, 3, 0, 1, 0)  # TEXTURE2D, one element
    return b'DDS ' + dds_header + dx10


def save_bc7(im, path):
    params = etcpak.BC7CompressBlockParams()
    blocks = []
    # Unity's raw texture data starts at the bottom row (ImageConversion.LoadImage flips PNGs itself).
    # Stored top-down, character atlases map the wrong regions: fur from the bottom of a boot texture
    # ended up on the shin. Ordinary DDS viewers therefore show these files upside down.
    for level in mip_chain(im.convert('RGBA').transpose(Image.FLIP_TOP_BOTTOM)):
        aligned = block_aligned(level)
        blocks.append(etcpak.compress_bc7(aligned.tobytes(), aligned.width, aligned.height, params))
    with open(path, 'wb') as f:
        f.write(header(im.width, im.height, len(blocks), len(blocks[0])))
        for b in blocks:
            f.write(b)
