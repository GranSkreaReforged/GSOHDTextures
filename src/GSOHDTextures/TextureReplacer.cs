using System.Collections.Generic;
using System.IO;
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
            public int Id;
            public bool Linear;
        }

        private struct Swap
        {
            public Material Material;
            public int Prop;
            public Texture Original;
        }

        // Trailing ! marks linear (non-colour) data.
        private static readonly string[] BuiltInProperties =
        {
            "_MainTex", "_BumpMap!", "_DetailAlbedoMap", "_DetailNormalMap!", "_DetailMask!", "_EmissionMap",
            "_MetallicGlossMap!", "_SpecGlossMap", "_OcclusionMap!", "_ParallaxMap!",
            "_Splat0", "_Splat1", "_Splat2", "_Splat3", "_Normal0!", "_Normal1!", "_Normal2!", "_Normal3!",
        };

        private readonly TextureStore store = new TextureStore();
        private readonly List<Swap> swaps = new List<Swap>();
        private readonly Dictionary<TerrainData, SplatPrototype[]> terrainSplats = new Dictionary<TerrainData, SplatPrototype[]>();
        private readonly Dictionary<TerrainData, DetailPrototype[]> terrainDetails = new Dictionary<TerrainData, DetailPrototype[]>();
        private readonly HashSet<string> seen = new HashSet<string>();
        private Prop[] props;
        private string seenFile;
        private bool scanPending = true;
        private float nextScan;

        private void Start()
        {
            props = ParseProperties();
            seenFile = Path.Combine(Paths.BepInExRootPath, "GSOHDTextures-seen.txt");
            if (Plugin.RecordSeenTextures.Value && File.Exists(seenFile))
                foreach (var line in File.ReadAllLines(seenFile))
                    seen.Add(line.Trim());

            store.Reload(Plugin.TextureDir);
            Plugin.Log.LogInfo($"{store.FileCount} replacement textures indexed.");
            SceneManager.sceneLoaded += (scene, mode) => scanPending = true;
        }

        private void Update()
        {
            if (Plugin.ReloadKey.Value.IsDown())
            {
                Restore();
                store.Reload(Plugin.TextureDir);
                Plugin.Log.LogInfo($"Reloaded: {store.FileCount} replacement textures indexed.");
                scanPending = true;
            }

            var interval = Plugin.ScanInterval.Value;
            if (scanPending || (interval > 0 && Time.unscaledTime >= nextScan))
            {
                scanPending = false;
                nextScan = Time.unscaledTime + Mathf.Max(interval, 0.25f);
                var before = store.LoadedCount;
                ScanMaterials();
                ScanTerrains();
                if (store.LoadedCount != before)
                    Plugin.Log.LogInfo($"Loaded {store.LoadedCount - before} textures ({store.LoadedCount}/{store.FileCount} in use).");
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
                result.Add(new Prop { Id = Shader.PropertyToID(linear ? name.Substring(0, name.Length - 1) : name), Linear = linear });
            }
            return result.ToArray();
        }

        private void ScanMaterials()
        {
            foreach (var mat in Resources.FindObjectsOfTypeAll<Material>())
            {
                foreach (var prop in props)
                {
                    if (!mat.HasProperty(prop.Id)) continue;
                    var replacement = Replace(mat.GetTexture(prop.Id), prop.Linear, out var original);
                    if (replacement == null) continue;
                    mat.SetTexture(prop.Id, replacement);
                    swaps.Add(new Swap { Material = mat, Prop = prop.Id, Original = original });
                }
            }
        }

        private void ScanTerrains()
        {
            foreach (var terrain in Terrain.activeTerrains)
            {
                var data = terrain.terrainData;
                if (data == null) continue;

                var splats = data.splatPrototypes;
                var changed = false;
                foreach (var sp in splats)
                {
                    var tex = Replace(sp.texture, false, out _);
                    if (tex != null) { sp.texture = tex; changed = true; }
                    var normal = Replace(sp.normalMap, true, out _);
                    if (normal != null) { sp.normalMap = normal; changed = true; }
                }
                if (changed)
                {
                    if (!terrainSplats.ContainsKey(data)) terrainSplats[data] = data.splatPrototypes;
                    data.splatPrototypes = splats;
                }

                var details = data.detailPrototypes;
                changed = false;
                foreach (var dp in details)
                {
                    var tex = Replace(dp.prototypeTexture, false, out _);
                    if (tex != null) { dp.prototypeTexture = tex; changed = true; }
                }
                if (changed)
                {
                    if (!terrainDetails.ContainsKey(data)) terrainDetails[data] = data.detailPrototypes;
                    data.detailPrototypes = details;
                }
            }
        }

        // The replacement for a texture the game is using, or null if there is none or it's already replaced.
        private Texture2D Replace(Texture tex, bool linear, out Texture2D original)
        {
            original = tex as Texture2D;
            if (original == null || store.IsReplacement(original)) return null;
            Record(original);
            return store.Get(original, linear);
        }

        private void Record(Texture2D tex)
        {
            if (!Plugin.RecordSeenTextures.Value) return;
            var key = TextureKey.Of(tex);
            if (seen.Add(key))
                File.AppendAllText(seenFile, key + "\n");
        }

        private void Restore()
        {
            foreach (var s in swaps)
                if (s.Material != null) s.Material.SetTexture(s.Prop, s.Original);
            swaps.Clear();
            foreach (var kv in terrainSplats)
                if (kv.Key != null) kv.Key.splatPrototypes = kv.Value;
            terrainSplats.Clear();
            foreach (var kv in terrainDetails)
                if (kv.Key != null) kv.Key.detailPrototypes = kv.Value;
            terrainDetails.Clear();
        }
    }
}
