using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace GSOHDTextures
{
    /// <summary>
    /// Indexes the replacement files on disk by texture key and loads them lazily, the first time
    /// the game uses the original. Each original is loaded at most once.
    /// </summary>
    internal class TextureStore
    {
        private readonly Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Original texture instance id -> replacement, or null when there is none (cached misses).
        private readonly Dictionary<int, Texture2D> byOriginal = new Dictionary<int, Texture2D>();
        private readonly HashSet<int> replacementIds = new HashSet<int>();

        public int FileCount => files.Count;
        public int LoadedCount => replacementIds.Count;

        public void Reload(string dir)
        {
            foreach (var tex in byOriginal.Values)
                if (tex != null) UnityEngine.Object.Destroy(tex);
            byOriginal.Clear();
            replacementIds.Clear();
            files.Clear();

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                return;
            }
            // Subfolders are allowed so packs can be organised; only the file name is the key.
            foreach (var path in Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories))
            {
                var key = Path.GetFileNameWithoutExtension(path);
                if (files.ContainsKey(key))
                    Plugin.Log.LogWarning($"Duplicate replacement for {key}; using {files[key]}, ignoring {path}");
                else
                    files[key] = path;
            }
        }

        public bool IsReplacement(Texture tex) => replacementIds.Contains(tex.GetInstanceID());

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
                    replacementIds.Add(result.GetInstanceID());
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

        private static Texture2D Load(string path, Texture2D original, bool linear)
        {
            var mips = original.mipmapCount > 1;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mips, linear);
            if (!tex.LoadImage(File.ReadAllBytes(path), false))
            {
                UnityEngine.Object.Destroy(tex);
                throw new Exception("not a valid PNG"); // Unity's Mono profile has no InvalidDataException
            }
            tex.name = original.name;
            tex.wrapMode = original.wrapMode;
            tex.filterMode = original.filterMode;
            tex.anisoLevel = original.anisoLevel;
            tex.mipMapBias = original.mipMapBias;

            if (mips) tex.Apply(true, false);
            // DXT needs multiple-of-4 sizes; upscaled power-of-two textures always qualify.
            if (Plugin.CompressTextures.Value && tex.width % 4 == 0 && tex.height % 4 == 0)
                tex.Compress(true);
            tex.Apply(false, true); // upload and free the CPU copy
            return tex;
        }
    }
}
