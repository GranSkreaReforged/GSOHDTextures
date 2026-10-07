using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GSOHDTextures
{
    /// <summary>
    /// Indexes the replacement files on disk by texture key and loads them lazily, the first time
    /// the game uses the original. Each original is loaded at most once.
    /// .dds files (what pack.ps1 builds: BC7/DXT1/DXT5 with mipmaps) are uploaded as-is, which is fast.
    /// .png files (hand-made) are decoded and compressed here, which stalls the game for large ones.
    /// </summary>
    internal class TextureStore
    {
        private readonly Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Original texture instance id -> replacement, or null when there is none (cached misses).
        private readonly Dictionary<int, Texture2D> byOriginal = new Dictionary<int, Texture2D>();
        // Replacement instance id -> the key it was loaded for.
        private readonly Dictionary<int, string> replacementKeys = new Dictionary<int, string>();

        public int FileCount => files.Count;
        public int LoadedCount => replacementKeys.Count;

        public void Reload(string dir)
        {
            foreach (var tex in byOriginal.Values)
                if (tex != null) UnityEngine.Object.Destroy(tex);
            byOriginal.Clear();
            replacementKeys.Clear();
            files.Clear();

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                return;
            }
            // Subfolders are allowed so packs can be organised; only the file name is the key.
            // PNGs are indexed last and win, so a hand-made PNG dropped in for testing beats the pack's DDS.
            var patterns = new[] { "*.dds", "*.png" };
            if (!SystemInfo.SupportsTextureFormat(TextureFormat.BC7))
            {
                Plugin.Log.LogWarning("This GPU or graphics API (DirectX 9?) has no BC7 support, so .dds textures are ignored.");
                patterns = new[] { "*.png" };
            }
            foreach (var pattern in patterns)
            {
                foreach (var path in Directory.GetFiles(dir, pattern, SearchOption.AllDirectories))
                {
                    var key = Path.GetFileNameWithoutExtension(path);
                    if (files.TryGetValue(key, out var other) && Path.GetExtension(other) == Path.GetExtension(path))
                        Plugin.Log.LogWarning($"Duplicate replacement for {key}; using {other}, ignoring {path}");
                    else
                        files[key] = path;
                }
            }
        }

        public bool IsReplacement(Texture tex) => replacementKeys.ContainsKey(tex.GetInstanceID());

        /// <summary>The key a replacement was loaded for, or null if <paramref name="tex"/> isn't one.</summary>
        public string KeyOf(Texture tex) => replacementKeys.TryGetValue(tex.GetInstanceID(), out var key) ? key : null;

        public bool HasFile(string key) => files.ContainsKey(key);

        /// <summary>Replacement for <paramref name="original"/>, or null. <paramref name="linear"/> is for normal maps and other non-colour data.</summary>
        public Texture2D Get(Texture2D original, bool linear)
        {
            var id = original.GetInstanceID();
            if (byOriginal.TryGetValue(id, out var cached))
                return cached;

            Texture2D result = null;
            var key = TextureKey.Of(original);
            if (files.TryGetValue(key, out var path))
            {
                try
                {
                    result = Load(path, original, linear);
                    replacementKeys[result.GetInstanceID()] = key;
                    if (Plugin.LogReplacements.Value)
                        Plugin.Log.LogInfo($"Replaced {key} -> {result.width}x{result.height}");
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Failed to load {path}: {e.Message}");
                }
            }
            byOriginal[id] = result;
            return result;
        }

        private static Texture2D Load(string path, Texture2D original, bool linear) =>
            path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? LoadDds(path, original, linear) : LoadPng(path, original, linear);

        private static void CopySettings(Texture2D tex, Texture2D original)
        {
            tex.name = original.name;
            tex.wrapMode = original.wrapMode;
            tex.filterMode = original.filterMode;
            tex.anisoLevel = original.anisoLevel;
            tex.mipMapBias = original.mipMapBias;
        }

        private const int DdsHeaderSize = 4 + 124;

        // Reads the DDS header (DX10 extension for BC7) and uploads the blocks without touching them.
        private static Texture2D LoadDds(string path, Texture2D original, bool linear)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < DdsHeaderSize || BitConverter.ToUInt32(bytes, 0) != 0x20534444) // "DDS "
                throw new Exception("not a DDS file");
            var height = BitConverter.ToInt32(bytes, 12);
            var width = BitConverter.ToInt32(bytes, 16);
            var mips = Math.Max(1, BitConverter.ToInt32(bytes, 28));
            var fourCC = System.Text.Encoding.ASCII.GetString(bytes, 84, 4);

            TextureFormat format;
            int blockBytes, offset = DdsHeaderSize;
            switch (fourCC)
            {
                case "DXT1": format = TextureFormat.DXT1; blockBytes = 8; break;
                case "DXT5": format = TextureFormat.DXT5; blockBytes = 16; break;
                case "DX10":
                    var dxgi = BitConverter.ToInt32(bytes, DdsHeaderSize);
                    if (dxgi != 98 && dxgi != 99) throw new Exception($"unsupported DXGI format {dxgi}"); // BC7_UNORM(_SRGB)
                    format = TextureFormat.BC7; blockBytes = 16; offset += 20; break;
                default: throw new Exception($"unsupported DDS format {fourCC}");
            }
            if (!SystemInfo.SupportsTextureFormat(format))
                throw new Exception($"this GPU or graphics API has no {format} support");

            // An original without mipmaps (UI, effects) gets only the top level.
            if (original.mipmapCount <= 1) mips = 1;
            var size = 0;
            for (var i = 0; i < mips; i++)
                size += ((Math.Max(1, width >> i) + 3) / 4) * ((Math.Max(1, height >> i) + 3) / 4) * blockBytes;
            if (bytes.Length < offset + size)
                throw new Exception("DDS file is truncated");

            var tex = new Texture2D(width, height, format, mips > 1, linear);
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { tex.LoadRawTextureData(new IntPtr(pin.AddrOfPinnedObject().ToInt64() + offset), size); }
            finally { pin.Free(); }
            CopySettings(tex, original);
            tex.Apply(false, true); // upload and free the CPU copy
            return tex;
        }

        private static Texture2D LoadPng(string path, Texture2D original, bool linear)
        {
            var mips = original.mipmapCount > 1;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mips, linear);
            if (!tex.LoadImage(File.ReadAllBytes(path), false))
            {
                UnityEngine.Object.Destroy(tex);
                throw new Exception("not a valid PNG"); // Unity's Mono profile has no InvalidDataException
            }
            CopySettings(tex, original);

            if (mips) tex.Apply(true, false);
            // DXT needs multiple-of-4 sizes; upscaled power-of-two textures always qualify.
            if (Plugin.CompressTextures.Value && tex.width % 4 == 0 && tex.height % 4 == 0)
                tex.Compress(true);
            tex.Apply(false, true); // upload and free the CPU copy
            return tex;
        }
    }
}
