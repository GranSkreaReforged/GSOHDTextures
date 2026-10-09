using System.Collections.Generic;
using UnityEngine;

namespace GSOHDTextures
{
    /// <summary>
    /// Lights and particles the game didn't have: street lamps and torches that light up, flicker on fires,
    /// glowing windows at night, lightning in storms, and ambient particles (AmbientParticles). Everything is
    /// found by name in each scene after it loads and attached to the objects it belongs to, so it switches
    /// on and off with the game's own area streaming and LODs.
    /// </summary>
    internal class Ambience : MonoBehaviour
    {
        internal static Ambience Instance { get; private set; }

        private static readonly Color LampColor = new Color(1f, 0.62f, 0.3f);
        private static readonly Color WindowColor = new Color(1f, 0.6f, 0.28f);
        // Materials that are only window glass. ("WindowB" is not: the windmill's whole body uses it.)
        private static readonly string[] WindowMaterials = { "Window", "glass_b_inside", "glass_a_outside", "glass_a_inside" };

        private class Flicker
        {
            public Light light;
            public float baseIntensity, seed;
            public bool atNight, gentle;   // atNight: off by day (our lamps); gentle: glass lanterns
        }

        private readonly List<Flicker> flickers = new List<Flicker>();
        private readonly List<GameObject> lampGlows = new List<GameObject>();
        private readonly List<Renderer> litWindows = new List<Renderer>();
        private float windowNight = -1f;

        private int sceneKey = int.MinValue;
        private float scanAt = -1f, nextSceneCheck;
        private Lightning lightning;
        private AmbientParticles particles;

        private void Start()
        {
            Instance = this;
            if (Plugin.AmbLightning.Value) lightning = new Lightning();
            particles = new AmbientParticles(transform);
        }

        // ---- shared state ----------------------------------------------------------------

        /// <summary>0 by day, 1 once the sun is well down; lights come on a little before sunset.</summary>
        internal static float Night
        {
            get
            {
                var sky = TOD_Sky.Instance;
                return sky == null ? 0f : Mathf.Clamp01((sky.SunZenith - 84f) / 8f);
            }
        }

        internal static bool Outdoors
        {
            get
            {
                var w = Script_WeatherController.instance;
                return w != null && w.target != null && !w.isUnderground;
            }
        }

        internal static float Cloudiness => Script_WeatherController.instance != null ? Script_WeatherController.instance.cloudiness : 0f;

        internal static Transform Player =>
            Scr_PlayerHandler.instance != null && Scr_PlayerHandler.instance.player != null ? Scr_PlayerHandler.instance.player.transform : null;

        // ---- per frame -------------------------------------------------------------------

        private void Update()
        {
            if (Time.unscaledTime >= nextSceneCheck)
            {
                nextSceneCheck = Time.unscaledTime + 1f;
                var sm = Script_sceneManager.instance;
                if (sm != null && !sm.loadingInProgress && sm.currentScene != null && sm.currentScene.id != sceneKey && Player != null)
                {
                    sceneKey = sm.currentScene.id;
                    flickers.Clear();
                    lampGlows.Clear();
                    litWindows.Clear();
                    foreach (var lit in litCopies.Values) if (lit != null) Destroy(lit);
                    litCopies.Clear();
                    windowNight = -1f;
                    scanAt = Time.unscaledTime + 2f;   // let the scene's areas switch on first
                }
            }
            if (scanAt > 0f && Time.unscaledTime >= scanAt)
            {
                scanAt = -1f;
                StartCoroutine(Scan());
            }

            float night = Night;
            UpdateLights(night);
            UpdateWindows(night);
            lightning?.Update();
            particles.ProbeChimneys();
            particles.Update(night);
        }

        // ---- scene scan --------------------------------------------------------------------

        // Walking every renderer in a world scene takes ~100 ms, so the scan runs ~3 ms per frame.
        private System.Collections.IEnumerator Scan()
        {
            var t0 = Time.realtimeSinceStartup;
            int lamps = 0, torches = 0, fires = 0, windows = 0, frames = 1;
            var renderers = Resources.FindObjectsOfTypeAll<Renderer>();
            GameObject fireTemplate = null;
            Transform templateTorch = null;
            if (Plugin.AmbTorchFires.Value) FindTorchTemplate(renderers, out fireTemplate, out templateTorch);
            var glowTemplate = fireTemplate != null ? fireTemplate.transform.Find("Glow") : null;

            var windowSet = new HashSet<string>(WindowMaterials);
            particles.BeginScan();
            float until = Time.realtimeSinceStartup + 0.003f;
            foreach (var r in renderers)
            {
                if (Time.realtimeSinceStartup > until)
                {
                    yield return null;
                    frames++;
                    until = Time.realtimeSinceStartup + 0.003f;
                }
                if (r == null || !r.gameObject.scene.IsValid() || r is ParticleSystemRenderer) continue;
                particles.Consider(r);
                string n = r.name;
                if (Plugin.AmbLamps.Value && (n == "streetlamp_$Lod:0" || n == "StreetLamp_LOD0_$Lod:0"))
                {
                    AddLamp(r, glowTemplate);
                    lamps++;
                }
                else if (fireTemplate != null && n == "Torch_$Lod:0" && !HasFire(r.transform.root))
                {
                    AddTorchFire(r.transform, fireTemplate, templateTorch);
                    torches++;
                }
                if (Plugin.AmbWindows.Value && Lit(r))
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null && windowSet.Contains(mats[i].name))
                        {
                            mats[i] = LitCopy(mats[i]);
                            changed = true;
                        }
                    if (changed)
                    {
                        r.sharedMaterials = mats;
                        litWindows.Add(r);
                        windows++;
                    }
                }
            }
            particles.EndScan();
            yield return null;
            if (Plugin.AmbFlicker.Value) fires = FindFireLights();
            Plugin.Log.LogInfo($"[ambience] scene scan: {lamps} lamps, {torches} torches lit, {fires} fires flicker, {windows} lit window groups, {particles.Summary} (over {frames + 1} frames, {(Time.realtimeSinceStartup - t0) * 1000f:F0} ms)");
        }

        // Whether a building's windows are lit at night: a stable choice per position, so it doesn't change on reload.
        private static bool Lit(Renderer r)
        {
            var p = r.transform.position;
            float h = Mathf.Abs(Mathf.Sin(p.x * 12.9898f + p.z * 78.233f) * 43758.547f) % 1f;
            return h < Plugin.AmbWindowShare.Value;
        }

        // ---- lamps ---------------------------------------------------------------------

        private void AddLamp(Renderer lamp, Transform glowTemplate)
        {
            if (lamp.transform.Find("HD Lamp") != null) return;
            // The lamp head: near the top of the post. Bounds are only valid for active renderers; inactive
            // areas use the post's own height from its mesh.
            var mf = lamp.GetComponent<MeshFilter>();
            float height = lamp.gameObject.activeInHierarchy ? lamp.bounds.max.y - lamp.transform.position.y
                : mf != null && mf.sharedMesh != null && !lamp.isPartOfStaticBatch ? mf.sharedMesh.bounds.max.y * lamp.transform.lossyScale.y : 3.6f;
            if (height < 1.5f || height > 8f) height = 3.6f;
            var head = new GameObject("HD Lamp");
            head.transform.SetParent(lamp.transform, false);
            head.transform.position = lamp.transform.position + Vector3.up * (height - 0.45f);

            // Lamp posts placed as prefabs often have a light already; only the glow is missing then.
            bool hasLight = false;
            foreach (var l in lamp.transform.root.GetComponentsInChildren<Light>(true))
                if (Vector3.Distance(l.transform.position, head.transform.position) < 3f) { hasLight = true; Register(l, false, true); }
            if (!hasLight)
            {
                var light = head.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = LampColor;
                light.range = 11f;
                light.intensity = 0f;
                light.shadows = LightShadows.None;
                Register(light, true, true, 1.5f);
            }
            if (glowTemplate != null)
            {
                var glow = Instantiate(glowTemplate.gameObject, head.transform, false);
                glow.name = "HD Lamp Glow";
                glow.transform.localPosition = Vector3.zero;
                glow.transform.localScale = Vector3.one * 0.6f;
                glow.SetActive(false);
                lampGlows.Add(glow);
            }
        }

        // ---- torches ---------------------------------------------------------------------

        // A lit standing torch from this scene: its "Fire_03" child holds the light, flame, sparks, glow and smoke.
        private static void FindTorchTemplate(Renderer[] renderers, out GameObject fire, out Transform torch)
        {
            fire = null;
            torch = null;
            foreach (var r in renderers)
            {
                if (r == null || r.name != "Torch_$Lod:0" || !r.gameObject.scene.IsValid()) continue;
                foreach (var ps in r.transform.root.GetComponentsInChildren<ParticleSystem>(true))
                    if (ps.name.StartsWith("Fire") && ps.GetComponent<Light>() != null)
                    {
                        fire = ps.gameObject;
                        torch = r.transform;
                        return;
                    }
            }
        }

        private static bool HasFire(Transform root)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                if (ps.name.StartsWith("Fire") || ps.name.StartsWith("Flame") || ps.name.StartsWith("HD Torch")) return true;
            return false;
        }

        private void AddTorchFire(Transform torch, GameObject fireTemplate, Transform templateTorch)
        {
            var fire = Instantiate(fireTemplate, torch.parent != null ? torch.parent : torch, false);
            fire.name = "HD Torch Fire";
            // Same place on this torch as the template's fire on its torch.
            fire.transform.position = torch.TransformPoint(templateTorch.InverseTransformPoint(fireTemplate.transform.position));
            fire.transform.rotation = torch.rotation * Quaternion.Inverse(templateTorch.rotation) * fireTemplate.transform.rotation;
            var l = fire.GetComponent<Light>();
            if (l != null) Register(l, false, false);
        }

        // ---- flicker ---------------------------------------------------------------------

        private int FindFireLights()
        {
            int n = 0;
            foreach (var l in Resources.FindObjectsOfTypeAll<Light>())
            {
                if (l == null || l.type != LightType.Point || !l.gameObject.scene.IsValid()) continue;
                string name = l.name.ToLowerInvariant();
                bool fire = name.StartsWith("fire") || name.Contains("torch") || name.Contains("candle") || name.Contains("brazier") || name.Contains("lantern");
                if (!fire)
                    foreach (var psr in l.GetComponentsInChildren<ParticleSystemRenderer>(true))
                        if (psr.sharedMaterial != null && (psr.sharedMaterial.name.Contains("Fire") || psr.sharedMaterial.name.Contains("Flame"))) { fire = true; break; }
                if (fire && Register(l, false, name.Contains("lantern"))) n++;
            }
            return n;
        }

        private bool Register(Light l, bool atNight, bool gentle, float baseIntensity = -1f)
        {
            foreach (var f in flickers)
                if (f.light == l) return false;
            flickers.Add(new Flicker { light = l, baseIntensity = baseIntensity > 0f ? baseIntensity : l.intensity, seed = Random.value * 100f, atNight = atNight, gentle = gentle });
            return true;
        }

        private void UpdateLights(float night)
        {
            float t = Time.time;
            bool flicker = Plugin.AmbFlicker.Value;
            for (int i = flickers.Count - 1; i >= 0; i--)
            {
                var f = flickers[i];
                if (f.light == null) { flickers.RemoveAt(i); continue; }
                if (!f.light.isActiveAndEnabled) continue;
                float k = 1f;
                if (flicker)
                {
                    float depth = f.gentle ? 0.06f : 0.18f;
                    k = 1f - depth * Mathf.PerlinNoise(t * 7f, f.seed) - depth * 0.4f * Mathf.PerlinNoise(t * 23f, f.seed + 7f);
                }
                f.light.intensity = f.baseIntensity * k * (f.atNight ? night : 1f);
            }
            bool glow = night > 0.3f;
            for (int i = lampGlows.Count - 1; i >= 0; i--)
            {
                if (lampGlows[i] == null) { lampGlows.RemoveAt(i); continue; }
                if (lampGlows[i].activeSelf != glow) lampGlows[i].SetActive(glow);
            }
        }

        // ---- windows -----------------------------------------------------------------------

        // Lit buildings swap their window material for a copy with emission on, and only that slot: other materials
        // on the same mesh (walls) may have emission enabled with a black colour, and must stay dark. The pane
        // texture doubles as the emission map, so the lead frames stay darker than the glass.
        private static readonly Dictionary<Material, Material> litCopies = new Dictionary<Material, Material>();

        private static Material LitCopy(Material m)
        {
            if (litCopies.TryGetValue(m, out var lit) && lit != null) return lit;
            lit = new Material(m) { name = m.name + " (HD lit)" };
            if (lit.HasProperty("_EmissionColor"))
            {
                lit.EnableKeyword("_EMISSION");
                if (lit.HasProperty("_EmissionMap") && lit.mainTexture != null) lit.SetTexture("_EmissionMap", lit.mainTexture);
                lit.SetColor("_EmissionColor", Color.black);
                lit.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            litCopies[m] = lit;
            return lit;
        }

        private void UpdateWindows(float night)
        {
            if (litCopies.Count == 0 || Mathf.Abs(night - windowNight) < 0.02f) return;
            windowNight = night;
            var c = WindowColor * (1.6f * night);
            foreach (var lit in litCopies.Values)
                if (lit != null) lit.SetColor("_EmissionColor", c);
        }

        /// <summary>Dev: put the player 20 m in front of the nearest lit window group, facing it along the camera.</summary>
        internal void ViewWindow()
        {
            var player = Player;
            var cam = Camera.main;
            if (player == null || cam == null) return;
            Renderer best = null;
            float bestD = float.MaxValue;
            foreach (var r in litWindows)
            {
                if (r == null || !r.gameObject.activeInHierarchy || r.name.Contains("Lod:1") || r.name.Contains("Lod:2") || r.name.Contains("Lod:3")) continue;
                float d = (r.bounds.center - player.position).sqrMagnitude;
                if (d > 25f && d < bestD) { bestD = d; best = r; }
            }
            if (best == null) return;
            var fwd = cam.transform.forward;
            fwd.y = 0f;
            var at = best.bounds.center - fwd.normalized * (best.bounds.extents.magnitude + 20f);
            var t = Terrain.activeTerrain;
            if (t != null) at.y = t.SampleHeight(at) + t.transform.position.y + 0.5f;
            player.position = at;
            Plugin.Log.LogInfo($"[dev] viewing lit windows '{best.name}' on '{best.transform.root.name}' from {at}");
        }

        /// <summary>Dev: put the player 25 m from the nearest detected chimney, looking at it along the camera.</summary>
        internal void ViewChimney()
        {
            var player = Player;
            var cam = Camera.main;
            if (player == null || cam == null || !particles.Nearest(player.position, out var chimney)) return;
            var fwd = cam.transform.forward;
            fwd.y = 0f;
            var at = chimney - fwd.normalized * 25f;
            var t = Terrain.activeTerrain;
            if (t != null) at.y = t.SampleHeight(at) + t.transform.position.y + 0.5f;
            player.position = at;
            Plugin.Log.LogInfo($"[dev] viewing chimney at {chimney} from {at}");
        }

        internal void LogChimneys(int n) => particles.LogChimneys(Player != null ? Player.position : Vector3.zero, n);

        /// <summary>Dev: the nearest lamp heads to the player.</summary>
        internal void LogLamps(int n)
        {
            var p = Player != null ? Player.position : Vector3.zero;
            var heads = new List<Vector3>();
            foreach (var g in lampGlows) if (g != null) heads.Add(g.transform.position);
            heads.Sort((a, b) => (a - p).sqrMagnitude.CompareTo((b - p).sqrMagnitude));
            for (int i = 0; i < heads.Count && i < n; i++)
                Plugin.Log.LogInfo($"[dev] lamp at {heads[i].x:F0} {heads[i].y:F0} {heads[i].z:F0} ({Vector3.Distance(heads[i], p):F0} m)");
        }

        internal string Status => $"{flickers.Count} flickering lights, {lampGlows.Count} lamp glows, {litWindows.Count} lit window groups, night {Night:F2}, {particles.Summary}";

        internal void Strike() => (lightning ?? (lightning = new Lightning())).Strike();
    }
}
