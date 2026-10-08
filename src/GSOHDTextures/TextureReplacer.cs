using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GSOHDTextures
{
    /// <summary>
    /// Points materials and terrains at HD replacements. Unity 2017.4 has no API to list a material's
    /// texture properties, so a fixed list of common names (plus config extras) is checked.
    /// Swaps are remembered so that a reload (F9) can restore the originals first.
    /// </summary>
    internal class TextureReplacer : MonoBehaviour
    {
        private struct Prop
        {
            public string Name;
            public int Id;
            public bool Linear;
        }

        private struct Swap
        {
            public Material Material;
            public int Prop;
            public Texture Original;
            public bool Linear;
        }

        // Trailing ! marks linear (non-colour) data. From tools/textures/props.py over the game's materials;
        // lookup textures (projectors, LUTs, dithering, water colour ramps) are left out on purpose.
        private static readonly string[] BuiltInProperties =
        {
            // Unity Standard and terrain
            "_MainTex", "_BumpMap!", "_DetailAlbedoMap", "_DetailNormalMap!", "_DetailMask!", "_EmissionMap",
            "_MetallicGlossMap!", "_SpecGlossMap", "_OcclusionMap!", "_ParallaxMap!",
            "_Splat0", "_Splat1", "_Splat2", "_Splat3", "_Normal0!", "_Normal1!", "_Normal2!", "_Normal3!",
            // SpeedTree and Tree Creator
            "_DetailTex", "_BumpSpecMap!", "_TranslucencyMap!",
            // CW3 environment blend
            "_Mask!", "_BlendDiffuse2", "_BlendNormal2!", "_DetailAlbedo", "_DetailNormal!", "_Heightmap!",
            // AQUAS water
            "_FoamTexture", "_SmallWavesTexture!", "_MediumWavesTexture!", "_LargeWavesTexture!",
            // KriptoFX effects
            "_DistortTex!", "_BumpTex!",
            // Assorted asset-store shaders
            "_AO!", "_layer1Tex", "_layer1Norm!", "_HeightMap!", "_Metallic!", "_NormalMap!", "_SnowBasecolor",
            "_MetallicTex!", "_NormalTex!", "_TopTex", "_flame_basecolor", "_Utils_map!",
            // Skybox/6 Sided
            "_FrontTex", "_BackTex", "_LeftTex", "_RightTex", "_UpTex", "_DownTex",
        };

        private readonly TextureStore store = new TextureStore();
        private readonly List<Swap> swaps = new List<Swap>();
        private readonly Dictionary<TerrainData, SplatPrototype[]> terrainSplats = new Dictionary<TerrainData, SplatPrototype[]>();
        private readonly HashSet<string> seen = new HashSet<string>();
        // HasProperty depends only on the shader, so each shader is checked against the list once.
        private readonly Dictionary<Shader, Prop[]> shaderProps = new Dictionary<Shader, Prop[]>();
        private Prop[] props;
        private string seenFile;
        private bool scanPending = true;
        private float nextScan;

        // Between scene loads, materials are checked a slice at a time (SliceBudgetMs per frame) and new
        // textures stream in from a background thread, so nothing during play costs a whole frame.
        private const double SliceBudgetMs = 1.0;
        private Material[] cycle;
        private int cursor;
        private int streamed;
        // Material slots whose replacement is still being read; filled in as soon as it's uploaded.
        private readonly List<Swap> waiting = new List<Swap>();

        internal static TextureReplacer Instance { get; private set; }

        private void Start()
        {
            Instance = this;
            props = ParseProperties();
            seenFile = Path.Combine(Paths.BepInExRootPath, "GSOHDTextures-seen.txt");
            if (Plugin.RecordSeenTextures.Value && File.Exists(seenFile))
                foreach (var line in File.ReadAllLines(seenFile))
                    seen.Add(line.Trim());

            store.Reload(Plugin.TextureDir);
            Plugin.Log.LogInfo($"{store.FileCount} replacement textures indexed.");
            SceneManager.sceneLoaded += (scene, mode) => scanPending = true;
        }

        private bool paused;

        /// <summary>Shows the original textures (paused) or the HD ones again, for side-by-side comparisons.</summary>
        internal void SetPaused(bool value)
        {
            paused = value;
            if (paused) Restore();
            else scanPending = true;
            Plugin.Log.LogInfo($"HD textures {(paused ? "paused (originals shown)" : "back on")}.");
        }

        private void Update()
        {
            if (paused) return;
            if (Plugin.ReloadKey.Value.IsDown())
            {
                Restore();
                store.Reload(Plugin.TextureDir);
                Plugin.Log.LogInfo($"Reloaded: {store.FileCount} replacement textures indexed.");
                scanPending = true;
            }

            var clock = System.Diagnostics.Stopwatch.StartNew();
            var interval = Plugin.ScanInterval.Value;
            if (scanPending)
            {
                // Scene load: the loading screen is up, so do everything now and arrive with the scene sharp.
                scanPending = false;
                cycle = null;
                nextScan = Time.unscaledTime + Mathf.Max(interval, 0.25f);
                var before = store.LoadedCount;
                var all = Resources.FindObjectsOfTypeAll<Material>();
                for (int i = 0; i < all.Length; i++) ScanMaterial(all[i], false);
                ScanTerrains(false);
                if (store.LoadedCount != before)
                    Plugin.Log.LogInfo($"Loaded {store.LoadedCount - before} textures in {clock.ElapsedMilliseconds} ms ({store.LoadedCount}/{store.FileCount} in use).");
            }
            else
            {
                if (store.Pump()) { streamed++; FillWaiting(); }
                if (cycle == null && interval > 0 && Time.unscaledTime >= nextScan)
                {
                    cycle = Resources.FindObjectsOfTypeAll<Material>();
                    cursor = 0;
                }
                if (cycle != null)
                {
                    while (cursor < cycle.Length && clock.Elapsed.TotalMilliseconds < SliceBudgetMs)
                        ScanMaterial(cycle[cursor++], true);
                    if (cursor >= cycle.Length)
                    {
                        ScanTerrains(true);
                        cycle = null;
                        nextScan = Time.unscaledTime + Mathf.Max(interval, 0.25f);
                    }
                }
                if (streamed > 0 && store.PendingCount == 0)
                {
                    Plugin.Log.LogInfo($"Streamed in {streamed} textures ({store.LoadedCount}/{store.FileCount} in use).");
                    streamed = 0;
                }
            }
            FrameProbe.PluginMsThisFrame += (float)clock.Elapsed.TotalMilliseconds;
        }

        private void FillWaiting()
        {
            for (int i = waiting.Count - 1; i >= 0; i--)
            {
                var w = waiting[i];
                var original = (Texture2D)w.Original;
                if (w.Material == null || original == null) { waiting.RemoveAt(i); continue; }
                if (!store.TryGetAsync(original, w.Linear, out var replacement)) continue;
                waiting.RemoveAt(i);
                // Only if the game hasn't put something else there in the meantime.
                if (replacement == null || w.Material.GetTexture(w.Prop) != original) continue;
                w.Material.SetTexture(w.Prop, replacement);
                swaps.Add(new Swap { Material = w.Material, Prop = w.Prop, Original = original });
            }
        }

        private Prop[] ParseProperties()
        {
            var names = new List<string>(BuiltInProperties);
            foreach (var extra in Plugin.ExtraProperties.Value.Split(','))
                if (extra.Trim().Length > 0) names.Add(extra.Trim());

            var result = new List<Prop>();
            foreach (var name in names)
            {
                var linear = name.EndsWith("!");
                var bare = linear ? name.Substring(0, name.Length - 1) : name;
                result.Add(new Prop { Name = bare, Id = Shader.PropertyToID(bare), Linear = linear });
            }
            return result.ToArray();
        }

        private void ScanMaterial(Material mat, bool async)
        {
            if (mat == null) return;   // destroyed since the cycle started
            var shader = mat.shader;
            if (shader == null) return;
            if (!shaderProps.TryGetValue(shader, out var has))
                shaderProps[shader] = has = props.Where(p => mat.HasProperty(p.Id)).ToArray();
            foreach (var prop in has)
            {
                var replacement = Replace(mat.GetTexture(prop.Id), prop.Linear, async, out var original, out bool pending);
                if (pending)
                {
                    if (!waiting.Exists(w => w.Material == mat && w.Prop == prop.Id))
                        waiting.Add(new Swap { Material = mat, Prop = prop.Id, Original = original, Linear = prop.Linear });
                    continue;
                }
                if (replacement == null) continue;
                mat.SetTexture(prop.Id, replacement);
                swaps.Add(new Swap { Material = mat, Prop = prop.Id, Original = original });
            }
        }

        // Terrain textures that are still streaming are picked up by the next cycle.
        private void ScanTerrains(bool async)
        {
            foreach (var terrain in Terrain.activeTerrains)
            {
                var data = terrain.terrainData;
                if (data == null) continue;

                var splats = data.splatPrototypes;
                var changed = false;
                foreach (var sp in splats)
                {
                    var tex = Replace(sp.texture, false, async, out var original, out _);
                    // Keep "no alpha" originals alpha-free, or the terrain reads the alpha as smoothness (see OpaqueCopy).
                    if (tex != null && TextureStore.HasNoAlpha(original.format)) tex = store.OpaqueCopy(tex);
                    if (tex != null) { sp.texture = tex; changed = true; }
                    var normal = Replace(sp.normalMap, true, async, out _, out _);
                    if (normal != null) { sp.normalMap = normal; changed = true; }
                }
                if (changed)
                {
                    if (!terrainSplats.ContainsKey(data)) terrainSplats[data] = data.splatPrototypes;
                    data.splatPrototypes = splats;
                }
                // Grass (detail prototype) textures are left alone: Unity packs them into an atlas on the CPU,
                // which needs readable, uncompressed pixels, and our BC7 uploads turn the grass into coloured noise.
            }
        }

        /// <summary>HD version of an interface image drawn with GUI.DrawTexture, or null. Called every frame, so it only does lookups after the first time.</summary>
        internal Texture2D UiReplacement(Texture tex) => Replace(tex, false, false, out _, out _);

        // The replacement for a texture the game is using, or null if there is none, it's already replaced,
        // or (async) it's still being read, which sets `pending`.
        private Texture2D Replace(Texture tex, bool linear, bool async, out Texture2D original, out bool pending)
        {
            pending = false;
            original = tex as Texture2D;
            if (original == null || store.IsReplacement(original)) return null;
            Record(original);
            if (!async) return store.Get(original, linear);
            pending = !store.TryGetAsync(original, linear, out var result);
            return result;
        }

        private void Record(Texture2D tex)
        {
            if (!Plugin.RecordSeenTextures.Value) return;
            var key = TextureKey.Of(tex);
            if (seen.Add(key))
                File.AppendAllText(seenFile, key + "\n");
        }

        /// <summary>One line per checked texture property of <paramref name="mat"/>: what it shows now, and the original key.</summary>
        internal IEnumerable<string> Describe(Material mat)
        {
            foreach (var prop in props)
            {
                if (!mat.HasProperty(prop.Id)) continue;
                var tex = mat.GetTexture(prop.Id) as Texture2D;
                if (tex == null) continue;
                if (!store.IsReplacement(tex))
                {
                    yield return $"{prop.Name} = {TextureKey.Of(tex)} (original{(store.HasFile(TextureKey.Of(tex)) ? ", has a file" : "")})";
                    continue;
                }
                yield return $"{prop.Name} = HD {tex.width}x{tex.height} for {store.KeyOf(tex)}";
            }
        }

        private void Restore()
        {
            waiting.Clear();
            cycle = null;
            foreach (var s in swaps)
                if (s.Material != null) s.Material.SetTexture(s.Prop, s.Original);
            swaps.Clear();
            foreach (var kv in terrainSplats)
                if (kv.Key != null) kv.Key.splatPrototypes = kv.Value;
            terrainSplats.Clear();
        }
    }
}
