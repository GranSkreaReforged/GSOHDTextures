using System.Collections.Generic;
using UnityEngine;

namespace GSOHDTextures
{
    /// <summary>
    /// Ambient particles around the player: fireflies on dry nights, dust and pollen on fair days, mist over the sea
    /// in the early morning and in fog, leaves falling from broadleaf trees, and smoke from chimneys. Each kind is
    /// one world-space particle system that emits only near the player; textures are generated at startup.
    /// </summary>
    internal class AmbientParticles
    {
        private const float SeaLevel = 3.2f;   // the AQUAS sea plane in the overworld
        private static readonly Vector3 Wind = new Vector3(1f, 0f, 0.35f).normalized;
        private static readonly string[] Broadleaf = { "oak", "birch", "maple", "beech", "elm", "broadleaf", "tree_" };
        private static readonly string[] Conifer = { "spruce", "pine", "fir", "palm", "stump", "log", "dead" };

        private readonly ParticleSystem fireflies, dust, mist, leaves, smoke;
        private readonly List<Vector3> trees = new List<Vector3>();
        private readonly List<Vector3> chimneys = new List<Vector3>();
        private readonly HashSet<string> treeNames = new HashSet<string>();
        private float fireflyDebt, dustDebt, mistDebt, leafDebt, smokeDebt;

        internal AmbientParticles(Transform host)
        {
            var dot = MakeDot(32, 2.2f);
            var blob = MakeBlob(64);
            var leaf = MakeLeaf(32);
            fireflies = Make(host, "HD Fireflies", "Particles/Additive", dot, 300, 0.6f, 0.35f);
            dust = Make(host, "HD Dust", "Particles/Additive", dot, 400, 0.12f, 0.15f);
            mist = Make(host, "HD Mist", "Particles/Alpha Blended", blob, 300, 0f, 0f);
            leaves = Make(host, "HD Leaves", "Particles/Alpha Blended", leaf, 200, 0.9f, 0.5f);
            smoke = Make(host, "HD Smoke", "Particles/Alpha Blended", blob, 400, 0.25f, 0.2f);
            Fade(fireflies, new[] { 0f, 1f, 0.35f, 1f, 0f }, new[] { 0f, 0.2f, 0.5f, 0.75f, 1f });
            Fade(dust, new[] { 0f, 1f, 1f, 0f }, new[] { 0f, 0.25f, 0.75f, 1f });
            Fade(mist, new[] { 0f, 1f, 1f, 0f }, new[] { 0f, 0.3f, 0.7f, 1f });
            Fade(leaves, new[] { 0f, 1f, 1f, 0f }, new[] { 0f, 0.05f, 0.85f, 1f });
            Fade(smoke, new[] { 0f, 1f, 0.6f, 0f }, new[] { 0f, 0.1f, 0.5f, 1f });
            var grow = smoke.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.3f, 1f, 1f));
        }

        internal string Summary => $"{trees.Count} broadleaf trees, {chimneys.Count} chimneys; live particles: fireflies {fireflies.particleCount}, dust {dust.particleCount}, mist {mist.particleCount}, leaves {leaves.particleCount}, smoke {smoke.particleCount}";

        internal bool Nearest(Vector3 p, out Vector3 chimney)
        {
            chimney = Vector3.zero;
            float best = float.MaxValue;
            foreach (var c in chimneys)
            {
                float d = (c - p).sqrMagnitude;
                if (d < best) { best = d; chimney = c; }
            }
            return best < float.MaxValue;
        }

        /// <summary>Dev: the nearest chimneys found by the roof probe.</summary>
        internal void LogChimneys(Vector3 p, int n)
        {
            var list = new List<Vector3>(chimneys);
            list.Sort((a, b) => (a - p).sqrMagnitude.CompareTo((b - p).sqrMagnitude));
            for (int i = 0; i < list.Count && i < n; i++)
                Plugin.Log.LogInfo($"[dev] chimney at {list[i].x:F0} {list[i].y:F0} {list[i].z:F0} ({Vector3.Distance(list[i], p):F0} m)");
            var t = new List<Vector3>(trees);
            t.Sort((a, b) => (a - p).sqrMagnitude.CompareTo((b - p).sqrMagnitude));
            for (int i = 0; i < t.Count && i < n; i++)
                Plugin.Log.LogInfo($"[dev] tree canopy at {t[i].x:F0} {t[i].y:F0} {t[i].z:F0} ({Vector3.Distance(t[i], p):F0} m)");
            Plugin.Log.LogInfo($"[dev] tree names: {string.Join(", ", new List<string>(treeNames).ToArray())}");
        }

        // ---- systems -----------------------------------------------------------------------

        private static ParticleSystem Make(Transform host, string name, string shader, Texture2D tex, int max, float noise, float noiseFrequency)
        {
            var go = new GameObject(name);
            go.transform.SetParent(host, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            var emission = ps.emission;
            emission.enabled = false;   // emitted by hand, where and when it fits
            var shape = ps.shape;
            shape.enabled = false;
            if (noise > 0f)
            {
                var n = ps.noise;
                n.enabled = true;
                n.strength = noise;
                n.frequency = noiseFrequency;
                n.scrollSpeed = 0.2f;
                n.damping = true;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = new Material(Shader.Find(shader)) { mainTexture = tex };
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.maxParticleSize = 5f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        private static void Fade(ParticleSystem ps, float[] alpha, float[] times)
        {
            var g = new Gradient();
            var keys = new GradientAlphaKey[alpha.Length];
            for (int i = 0; i < alpha.Length; i++) keys[i] = new GradientAlphaKey(alpha[i], times[i]);
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, keys);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(g);
        }

        // ---- per frame -------------------------------------------------------------------

        internal void Update(float night)
        {
            var player = Ambience.Player;
            var cam = Camera.main;
            if (player == null || cam == null || !Ambience.Outdoors) return;
            float dt = Time.deltaTime, density = Plugin.AmbDensity.Value;
            float cloud = Ambience.Cloudiness;
            bool rain = cloud > 80f;
            float wind = Script_WeatherController.instance != null ? Script_WeatherController.instance.windSpeed : 0.3f;
            var p = player.position;

            if (Plugin.AmbFireflies.Value && !rain && night > 0.5f)
                for (int i = Due(ref fireflyDebt, 6f * density * night * dt); i > 0; i--) Firefly(p);
            if (Plugin.AmbDust.Value && cloud < 70f && night < 0.5f)
                for (int i = Due(ref dustDebt, 10f * density * (1f - night * 2f) * dt); i > 0; i--) Mote(cam.transform.position);
            float mistAmount = Mathf.Max(MorningMist(), Weather.Instance != null ? Weather.Instance.FogAmount : 0f);
            if (Plugin.AmbMist.Value && mistAmount > 0.05f)
                for (int i = Due(ref mistDebt, 7f * density * mistAmount * dt); i > 0; i--) Mist(p, wind, mistAmount);
            if (Plugin.AmbLeaves.Value && trees.Count > 0)
            {
                Near(trees, p, 35f);
                for (int i = Due(ref leafDebt, 0.35f * density * Mathf.Min(near.Count, 20) * dt); i > 0; i--) Leaf(wind);
            }
            if (Plugin.AmbSmoke.Value && chimneys.Count > 0)
            {
                Near(chimneys, p, 120f);
                for (int i = Due(ref smokeDebt, 2.5f * density * near.Count * dt); i > 0; i--) Smoke(wind, rain);
            }
        }

        // How many particles are due this frame (fractions carry over), at most 20.
        private static int Due(ref float debt, float amount)
        {
            debt += amount;
            int n = Mathf.Min(20, (int)debt);
            debt -= (int)debt;
            return n;
        }

        // Reused every frame, so emitting allocates nothing.
        private readonly List<Vector3> near = new List<Vector3>();

        private void Near(List<Vector3> points, Vector3 p, float radius)
        {
            near.Clear();
            float r2 = radius * radius;
            foreach (var v in points)
                if ((v - p).sqrMagnitude < r2) near.Add(v);
        }

        private static float Ground(Vector3 at)
        {
            var t = Terrain.activeTerrain;
            return t != null ? t.SampleHeight(at) + t.transform.position.y : at.y;
        }

        private static float MorningMist()
        {
            float h = Weather.Instance != null ? Weather.Instance.Hour : (TOD_Sky.Instance != null ? TOD_Sky.Instance.Cycle.Hour : 12f);
            return Mathf.Clamp01(1f - Mathf.Abs(h - 6.5f) / 2.5f);
        }

        private bool Firefly(Vector3 p)
        {
            var at = p + Random.insideUnitSphere * 22f;
            float ground = Ground(at);
            if (ground < SeaLevel) return false;
            at.y = ground + Random.Range(0.4f, 2.5f);
            fireflies.Emit(new ParticleSystem.EmitParams
            {
                position = at,
                velocity = Random.insideUnitSphere * 0.2f,
                startSize = Random.Range(0.07f, 0.13f),
                startLifetime = Random.Range(5f, 9f),
                startColor = new Color(0.8f, 1f, 0.35f, 1f),
            }, 1);
            return true;
        }

        private bool Mote(Vector3 cam)
        {
            var at = cam + Random.insideUnitSphere * 10f;
            if (at.y < Ground(at) + 0.2f) return false;
            dust.Emit(new ParticleSystem.EmitParams
            {
                position = at,
                velocity = Random.insideUnitSphere * 0.05f + Vector3.up * 0.02f,
                startSize = Random.Range(0.03f, 0.06f),
                startLifetime = Random.Range(6f, 10f),
                startColor = new Color(1f, 0.95f, 0.8f, 0.5f),
            }, 1);
            return true;
        }

        private bool Mist(Vector3 p, float wind, float amount)
        {
            var at = p + new Vector3(Random.Range(-60f, 60f), 0f, Random.Range(-60f, 60f));
            if (Ground(at) > SeaLevel - 0.3f) return false;   // only over water
            at.y = SeaLevel + Random.Range(0.2f, 1.2f);
            mist.Emit(new ParticleSystem.EmitParams
            {
                position = at,
                velocity = Wind * (0.3f + wind * 0.4f),
                startSize = Random.Range(12f, 20f),
                startLifetime = Random.Range(18f, 28f),
                startColor = new Color(0.86f, 0.89f, 0.93f, 0.22f + 0.12f * amount),
                rotation = Random.Range(0f, 360f),
            }, 1);
            return true;
        }

        private bool Leaf(float wind)
        {
            if (near.Count == 0) return false;
            var top = near[Random.Range(0, near.Count)];
            var at = top + new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(-1.5f, 0.5f), Random.Range(-2.5f, 2.5f));
            float shade = Random.value;
            var colour = shade < 0.5f ? new Color(0.45f, 0.58f, 0.2f) : shade < 0.8f ? new Color(0.82f, 0.66f, 0.22f) : new Color(0.55f, 0.34f, 0.15f);
            leaves.Emit(new ParticleSystem.EmitParams
            {
                position = at,
                velocity = Vector3.down * Random.Range(0.6f, 1f) + Wind * (0.2f + wind * 0.8f),
                startSize = Random.Range(0.12f, 0.2f),
                startLifetime = Mathf.Clamp((at.y - Ground(at)) / 0.75f, 2f, 12f),
                startColor = colour,
                rotation = Random.Range(0f, 360f),
                angularVelocity = Random.Range(-180f, 180f),
            }, 1);
            return true;
        }

        private bool Smoke(float wind, bool rain)
        {
            if (near.Count == 0) return false;
            var at = near[Random.Range(0, near.Count)];
            smoke.Emit(new ParticleSystem.EmitParams
            {
                position = at + Random.insideUnitSphere * 0.2f,
                velocity = Vector3.up * Random.Range(0.7f, 1.1f) + Wind * (0.15f + wind * 0.6f),
                startSize = Random.Range(1.6f, 2.6f),
                startLifetime = Random.Range(7f, 10f),
                startColor = rain ? new Color(0.5f, 0.5f, 0.52f, 0.25f) : new Color(0.62f, 0.62f, 0.64f, 0.3f),
                rotation = Random.Range(0f, 360f),
            }, 1);
            return true;
        }

        // ---- scene scan ------------------------------------------------------------------

        private readonly HashSet<Transform> seenTrees = new HashSet<Transform>();

        private bool scanning;
        internal void EndScan() => scanning = false;

        internal void BeginScan()
        {
            scanning = true;
            trees.Clear();
            chimneys.Clear();
            treeNames.Clear();
            pendingBuildings.Clear();
            seenTrees.Clear();
        }

        /// <summary>One renderer of the scene scan: is it a broadleaf tree, or a building to probe for a chimney?</summary>
        internal void Consider(Renderer r)
        {
            string n = r.name.ToLowerInvariant();
            if (n.Contains("$lod:") && !n.Contains("$lod:0")) return;   // one per LOD group
            if (n.Contains("_lod1") || n.Contains("_lod2") || n.Contains("billboard")) return;
            if (Plugin.AmbLeaves.Value && IsBroadleaf(r) && seenTrees.Add(r.transform.parent != null ? r.transform.parent : r.transform))
            {
                treeNames.Add(r.transform.root.name);
                float height = r.gameObject.activeInHierarchy ? r.bounds.size.y : 8f;
                trees.Add(r.transform.position + Vector3.up * Mathf.Clamp(height * 0.75f, 3f, 14f));
            }
            if (Plugin.AmbSmoke.Value && r.gameObject.activeInHierarchy)
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.name.StartsWith("Window")) { pendingBuildings.Enqueue(r.bounds); break; }
        }

        // Probing roofs takes thousands of rays, so it runs a few milliseconds per frame after the scan.
        private readonly Queue<Bounds> pendingBuildings = new Queue<Bounds>();

        internal void ProbeChimneys()
        {
            if (pendingBuildings.Count == 0) return;
            float until = Time.realtimeSinceStartup + 0.002f;
            while (pendingBuildings.Count > 0 && Time.realtimeSinceStartup < until)
                FindChimney(pendingBuildings.Dequeue());
            if (pendingBuildings.Count == 0 && !scanning) Plugin.Log.LogInfo($"[ambience] chimney probe done: {chimneys.Count} chimneys");
        }

        // Trees are named every which way; their leaf materials and tree prefabs are the reliable sign.
        private static bool IsBroadleaf(Renderer r)
        {
            string root = r.transform.root.name;
            if (root.StartsWith("NPC_")) return false;   // outfits with leaf materials
            // Harvestable trees: only oaks (type 10) shed leaves; spruces (1, 46) and palms (42) don't.
            if (root.StartsWith("Harvestable_")) return root.StartsWith("Harvestable_10 ");
            string names = (r.name + " " + root).ToLowerInvariant();
            bool leafy = names.Contains("prefab_terrain_tree");
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                string mn = m.name.ToLowerInvariant();
                names += " " + mn;
                if (mn.Contains("leaves") || mn.Contains("leaf")) leafy = true;
            }
            foreach (var b in Broadleaf) if (names.Contains(b)) leafy = true;
            if (!leafy) return false;
            foreach (var c in Conifer) if (names.Contains(c)) return false;
            return true;
        }

        // A chimney is the building's highest point standing clear of the roof: everything 1.5-2.5 m around it is
        // at least 0.7 m lower (a roof ridge fails that along its length). Probed with rays onto the colliders.
        private void FindChimney(Bounds b)
        {
            if (b.size.x > 40f || b.size.z > 40f || b.size.y < 3f) return;
            const float step = 0.5f;
            float best = float.MinValue;
            Vector3 top = Vector3.zero;
            for (float x = b.min.x; x <= b.max.x; x += step)
                for (float z = b.min.z; z <= b.max.z; z += step)
                {
                    float h = Probe(x, z, b.max.y + 3f);
                    if (h > best) { best = h; top = new Vector3(x, h, z); }
                }
            if (best == float.MinValue || best < b.min.y + 3f) return;
            for (int i = 0; i < 8; i++)
            {
                var d = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * 2f;
                if (Probe(top.x + d.x, top.z + d.z, b.max.y + 3f) > best - 0.7f) return;
            }
            foreach (var c in chimneys)
                if ((c - top).sqrMagnitude < 4f) return;
            chimneys.Add(top + Vector3.up * 0.3f);
        }

        private static float Probe(float x, float z, float fromY)
        {
            return Physics.Raycast(new Vector3(x, fromY, z), Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point.y : float.MinValue;
        }

        // ---- textures --------------------------------------------------------------------

        private static Texture2D MakeDot(int size, float sharpness)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                    float a = Mathf.Pow(Mathf.Clamp01(1f - d), sharpness);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        private static Texture2D MakeBlob(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                    float n = 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.12f, y * 0.12f);
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1f - d)) * n;
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }

        private static Texture2D MakeLeaf(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // A pointed oval along the diagonal with a darker midrib.
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    float along = (u + v) * 0.7071f, across = (u - v) * 0.7071f;
                    float width = 0.42f * (1f - along * along);
                    float inside = Mathf.Clamp01((width - Mathf.Abs(across)) * 14f);
                    float rib = Mathf.Abs(across) < 0.035f ? 0.75f : 1f;
                    t.SetPixel(x, y, new Color(rib, rib, rib, inside * (Mathf.Abs(along) < 0.95f ? 1f : 0f)));
                }
            t.Apply();
            return t;
        }
    }
}
