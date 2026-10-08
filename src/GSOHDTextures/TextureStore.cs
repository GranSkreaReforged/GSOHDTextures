using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace GSOHDTextures
{
    /// <summary>
    /// Indexes the replacement files on disk by texture key and loads them lazily, the first time
    /// the game uses the original. Each original is loaded at most once.
    /// .dds files (what pack.ps1 builds: BC7/DXT1/DXT5 with mipmaps) are uploaded as-is, which is fast.
    /// .png files (hand-made) are decoded and compressed here, which stalls the game for large ones.
    ///
    /// Two ways in: <see cref="Get"/> loads on the spot (scene loads, behind the loading screen), and
    /// <see cref="TryGetAsync"/> reads the file on a background thread and uploads it in a later frame
    /// (<see cref="Pump"/>), so textures that appear during play don't stall a frame. Files are read into
    /// reused buffers: allocating a fresh 20 MB array per texture made Mono's garbage collector pause the game.
    /// </summary>
    internal class TextureStore
    {
        private class Pending
        {
            public string Key, Path;
            public bool Linear;
            public Texture2D Template;   // any original with this key (for size, mipmaps and sampler settings)
            public readonly List<int> Originals = new List<int>();
        }

        private readonly Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Original texture instance id -> replacement, or null when there is none (cached misses).
        private readonly Dictionary<int, Texture2D> byOriginal = new Dictionary<int, Texture2D>();
        // Replacement instance id -> the key it was loaded for, and back.
        private readonly Dictionary<int, string> replacementKeys = new Dictionary<int, string>();
        private readonly Dictionary<string, Texture2D> byKey = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<Pending> queue = new Queue<Pending>();
        private byte[] syncBuffer = new byte[0], asyncBuffer = new byte[0];
        // Background read state, shared with the reader thread under `gate`.
        private readonly object gate = new object();
        private Pending reading;
        private int readLength = -1;    // -1 while reading, else bytes read into asyncBuffer
        private string readError;
        private int generation;         // bumped by Reload so a stale read is dropped

        public int FileCount => files.Count;
        public int LoadedCount => replacementKeys.Count;
        public int PendingCount => pending.Count;

        public void Reload(string dir)
        {
            foreach (var tex in byOriginal.Values)
                if (tex != null) UnityEngine.Object.Destroy(tex);
            byOriginal.Clear();
            replacementKeys.Clear();
            byKey.Clear();
            files.Clear();
            lock (gate)
            {
                generation++;
                pending.Clear();
                queue.Clear();
                reading = null;
            }

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

        /// <summary>Replacement for <paramref name="original"/>, or null, loading it now if needed. <paramref name="linear"/> is for normal maps and other non-colour data.</summary>
        public Texture2D Get(Texture2D original, bool linear)
        {
            var id = original.GetInstanceID();
            if (byOriginal.TryGetValue(id, out var cached))
                return cached;
            var key = TextureKey.Of(original);
            Texture2D result = null;
            if (files.TryGetValue(key, out var path))
            {
                var existing = Loaded(key);
                result = existing ?? Finish(key, path, () => LoadNow(path, original, linear));
            }
            byOriginal[id] = result;
            return result;
        }

        /// <summary>
        /// True with the replacement (or null if there is none) when it's known; false while its file is still
        /// being read in the background. Call again later (after <see cref="Pump"/> reports progress).
        /// </summary>
        public bool TryGetAsync(Texture2D original, bool linear, out Texture2D result)
        {
            var id = original.GetInstanceID();
            if (byOriginal.TryGetValue(id, out result))
                return true;
            var key = TextureKey.Of(original);
            if (!files.TryGetValue(key, out var path))
            {
                byOriginal[id] = null;
                return true;
            }
            var existing = Loaded(key);
            if (existing != null)
            {
                byOriginal[id] = result = existing;
                return true;
            }
            if (!path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            {
                result = Get(original, linear);   // hand-made PNGs are rare; decode them on the spot
                return true;
            }
            lock (gate)
            {
                if (!pending.TryGetValue(key, out var p))
                {
                    pending[key] = p = new Pending { Key = key, Path = path, Linear = linear, Template = original };
                    queue.Enqueue(p);
                }
                if (!p.Originals.Contains(id)) p.Originals.Add(id);
            }
            StartNextRead();
            return false;
        }

        /// <summary>Uploads a finished background read, if any. Returns true when a texture became available.</summary>
        public bool Pump()
        {
            Pending done;
            int length;
            string error;
            lock (gate)
            {
                if (reading == null || readLength < 0) return false;
                done = reading;
                length = readLength;
                error = readError;
                reading = null;
            }

            Texture2D result = null;
            if (error != null)
                Plugin.Log.LogError($"Failed to read {done.Path}: {error}");
            else if (done.Template != null)
                result = Finish(done.Key, done.Path, () => LoadDds(asyncBuffer, length, done.Template, done.Linear));
            lock (gate) pending.Remove(done.Key);
            foreach (var id in done.Originals) byOriginal[id] = result;
            StartNextRead();
            return true;
        }

        private void StartNextRead()
        {
            Pending next;
            int gen;
            lock (gate)
            {
                if (reading != null || queue.Count == 0) return;
                next = queue.Dequeue();
                reading = next;
                readLength = -1;
                readError = null;
                gen = generation;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                int n = -1;
                string err = null;
                try { n = ReadInto(next.Path, ref asyncBuffer); }
                catch (Exception e) { err = e.Message; n = 0; }
                lock (gate)
                {
                    if (gen != generation || reading != next) return;
                    readError = err;
                    readLength = n;
                }
            });
        }

        // An already-loaded replacement for this key (several originals can share one).
        private Texture2D Loaded(string key) => byKey.TryGetValue(key, out var t) ? t : null;

        private Texture2D Finish(string key, string path, Func<Texture2D> load)
        {
            try
            {
                var result = load();
                replacementKeys[result.GetInstanceID()] = key;
                byKey[key] = result;
                if (Plugin.LogReplacements.Value)
                    Plugin.Log.LogInfo($"Replaced {key} -> {result.width}x{result.height}");
                return result;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Failed to load {path}: {e.Message}");
                return null;
            }
        }

        private Texture2D LoadNow(string path, Texture2D original, bool linear)
        {
            if (!path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) return LoadPng(path, original, linear);
            int length = ReadInto(path, ref syncBuffer);
            return LoadDds(syncBuffer, length, original, linear);
        }

        // Reads a whole file into `buffer`, growing it only when a bigger file comes along.
        private static int ReadInto(string path, ref byte[] buffer)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                var length = (int)fs.Length;
                if (buffer.Length < length) buffer = new byte[Math.Max(length, buffer.Length * 2)];
                int read = 0;
                while (read < length)
                {
                    int n = fs.Read(buffer, read, length - read);
                    if (n <= 0) throw new IOException("unexpected end of file");
                    read += n;
                }
                return length;
            }
        }

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
        private static Texture2D LoadDds(byte[] bytes, int length, Texture2D original, bool linear)
        {
            if (length < DdsHeaderSize || BitConverter.ToUInt32(bytes, 0) != 0x20534444) // "DDS "
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
            if (length < offset + size)
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
