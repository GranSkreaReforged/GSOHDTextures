using System.Collections.Generic;
using UnityEngine;

namespace GSOHDTextures
{
    /// <summary>
    /// Commands for the GSODevTools DevBridge, found by reflection (this mod never references it).
    /// </summary>
    internal static class DevCommands
    {
        // The game puts the local player's meshes on this layer (Scr_skinner).
        private const int LocalPlayerLayer = 22;

        // frames [seconds]: frame-time statistics and the worst hitches, with the plugin's share of each.
        private static void Frames(string[] args)
        {
            FrameProbe.Run(args.Length > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 10f);
        }

        // weather: the game's weather controller (time, speed, clouds, rain, fog, wind) and the render fog.
        private static void Weather(string[] args)
        {
            var w = Script_WeatherController.instance;
            if (w == null)
            {
                Plugin.Log.LogInfo("[dev] weather: no Script_WeatherController");
                return;
            }
            Plugin.Log.LogInfo($"[dev] weather time={w.time:F0} timeSpeed={w.timeSpeed} cloudiness={w.cloudiness} temperature={w.temperature} wind={w.windSpeed} fogginess={w.fogginess} underground={w.isUnderground} target={(w.target != null ? w.target.name : "none")}");
            Plugin.Log.LogInfo($"[dev] weather rain rate={w.rainParticles.emission.rateOverTime.constant} particles={w.rainParticles.particleCount} audio={w.rainAudio.volume:F2}; snow rate={w.snowParticles.emission.rateOverTime.constant}");
            Plugin.Log.LogInfo($"[dev] weather fog on={RenderSettings.fog} mode={RenderSettings.fogMode} start={RenderSettings.fogStartDistance:F0} end={RenderSettings.fogEndDistance:F0} color={RenderSettings.fogColor} density={RenderSettings.fogDensity}");
            if (GSOHDTextures.Weather.Instance != null)
            {
                GSOHDTextures.Weather.Instance.Mood(out float exp, out float temp, out float sat, out float con);
                Plugin.Log.LogInfo($"[dev] weather mod pattern={GSOHDTextures.Weather.Instance.Current} hour={GSOHDTextures.Weather.Instance.Hour:F2} fog={GSOHDTextures.Weather.Instance.FogAmount:F2} mood exposure={exp:F2} temperature={temp:F1} saturation={sat:F2} contrast={con:F2}");
            }
            if (w.todSky != null)
                Plugin.Log.LogInfo($"[dev] weather sky hour={w.todSky.Cycle.Hour:F2} clouds={w.todSky.Clouds.Coverage:F2} fogginess={w.todSky.Atmosphere.Fogginess:F2} brightness={w.todSky.Atmosphere.Brightness:F2} sun={w.sunLight?.intensity} moon={w.moonLight?.intensity}");
        }

        // ambience [radius]: what lights and particle systems the scene has within radius (default 150 m) of the
        // player, and objects whose names suggest a light source (torch, lantern, candle, fire, window, chimney...).
        private static void Ambience(string[] args)
        {
            float radius = args.Length > 1 ? float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 150f;
            var player = Scr_PlayerHandler.instance != null && Scr_PlayerHandler.instance.player != null ? Scr_PlayerHandler.instance.player.transform.position : Vector3.zero;
            var counts = new SortedDictionary<string, int>();
            void Count(string key) => counts[key] = (counts.TryGetValue(key, out int n) ? n : 0) + 1;

            foreach (var l in Object.FindObjectsOfType<Light>())
            {
                if (l.type != LightType.Directional && Vector3.Distance(l.transform.position, player) > radius) continue;
                Count($"light {l.type} enabled={l.enabled && l.gameObject.activeInHierarchy} shadows={l.shadows} '{Strip(l.name)}' range={l.range:F0} intensity={l.intensity:F1}");
            }
            foreach (var p in Object.FindObjectsOfType<ParticleSystem>())
            {
                if (Vector3.Distance(p.transform.position, player) > radius) continue;
                Count($"particles '{Strip(p.name)}' under '{Strip(p.transform.root.name)}' playing={p.isPlaying}");
            }
            var words = new[] { "torch", "lantern", "lamp", "candle", "fire", "brazier", "window", "chimney", "smoke", "light", "glow", "crystal", "forge", "oven", "furnace" };
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                if (r is ParticleSystemRenderer || Vector3.Distance(r.transform.position, player) > radius) continue;
                string n = r.name.ToLowerInvariant();
                foreach (var w in words)
                    if (n.Contains(w)) { Count($"object '{Strip(r.name)}' ({w}) has light child={r.GetComponentInChildren<Light>() != null}"); break; }
            }
            foreach (var kv in counts) Plugin.Log.LogInfo($"[dev] ambience {kv.Value} x {kv.Key}");
            Plugin.Log.LogInfo($"[dev] ambience: {counts.Count} kinds within {radius} m of {player}");
        }

        // ambstatus: what the ambience module added in this scene.
        private static void AmbStatus(string[] args) =>
            Plugin.Log.LogInfo($"[dev] ambience {(GSOHDTextures.Ambience.Instance != null ? GSOHDTextures.Ambience.Instance.Status : "off")}");

        // amblamps [n]: the nearest lamps the ambience module lights.
        private static void AmbLamps(string[] args) => GSOHDTextures.Ambience.Instance?.LogLamps(args.Length > 1 ? int.Parse(args[1]) : 5);

        // mattex <material name>: the textures on each loaded material with that name (and their HD keys).
        private static void MatTex(string[] args)
        {
            var seen = new HashSet<Material>();
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null || m.name != args[1] || !seen.Add(m)) continue;
                foreach (var prop in new[] { "_MainTex", "_BumpMap", "_EmissionMap", "_MetallicGlossMap", "_OcclusionMap" })
                    if (m.HasProperty(prop) && m.GetTexture(prop) != null)
                    {
                        var t = m.GetTexture(prop);
                        Plugin.Log.LogInfo($"[dev] mattex '{m.name}' {prop} = '{t.name}' {t.width}x{t.height} shader '{m.shader.name}'");
                    }
            }
        }

        // ambwindow: moves the player 20 m in front of the nearest lit window group (along the camera) for a screenshot.
        private static void AmbWindow(string[] args) => GSOHDTextures.Ambience.Instance?.ViewWindow();

        // ambviewchimney: moves the player 25 m from the nearest detected chimney for a screenshot.
        private static void AmbViewChimney(string[] args) => GSOHDTextures.Ambience.Instance?.ViewChimney();

        // ambchimneys [n]: the nearest chimneys found by the roof probe.
        private static void AmbChimneys(string[] args) => GSOHDTextures.Ambience.Instance?.LogChimneys(args.Length > 1 ? int.Parse(args[1]) : 5);

        // lightning: a lightning strike now (flash, bolt, thunder).
        private static void Lightning(string[] args) => GSOHDTextures.Ambience.Instance?.Strike();

        // findnames <word>: objects, meshes and materials (with shader and emission support) whose names contain the word.
        private static void FindNames(string[] args)
        {
            string word = args[1].ToLowerInvariant();
            var counts = new SortedDictionary<string, int>();
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                var mf = r.GetComponent<MeshFilter>();
                string mesh = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-";
                bool hit = r.name.ToLowerInvariant().Contains(word) || mesh.ToLowerInvariant().Contains(word);
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    if (!hit && !m.name.ToLowerInvariant().Contains(word)) continue;
                    string key = $"'{Strip(r.name)}' mesh='{mesh}' material='{m.name}' shader='{m.shader.name}' emission={m.HasProperty("_EmissionColor")} root='{Strip(r.transform.root.name)}'";
                    counts[key] = (counts.TryGetValue(key, out int n) ? n : 0) + 1;
                }
            }
            foreach (var kv in counts) Plugin.Log.LogInfo($"[dev] findnames {kv.Value} x {kv.Key}");
            Plugin.Log.LogInfo($"[dev] findnames '{word}': {counts.Count} kinds");
        }

        // inspect <root name>: the first object with that root name, as a tree with components and positions;
        // plus particle shader availability, terrain trees and water surfaces.
        private static void Inspect(string[] args)
        {
            GameObject root = null;
            foreach (var t in Object.FindObjectsOfType<Transform>())
                if (t.parent == null && Strip(t.name) == args[1]) { root = t.gameObject; break; }
            if (root != null) Dump(root.transform, 0);
            else Plugin.Log.LogInfo($"[dev] inspect: no root '{args[1]}'");

            foreach (var s in new[] { "Particles/Additive", "Particles/Alpha Blended", "Particles/Alpha Blended Premultiply", "Particles/Standard Unlit", "Legacy Shaders/Particles/Alpha Blended", "Mobile/Particles/Alpha Blended" })
                Plugin.Log.LogInfo($"[dev] inspect shader '{s}' found={Shader.Find(s) != null}");
            foreach (var terrain in Terrain.activeTerrains)
                Plugin.Log.LogInfo($"[dev] inspect terrain '{terrain.name}' at {terrain.transform.position} size {terrain.terrainData.size} trees {terrain.terrainData.treeInstanceCount} prototypes {terrain.terrainData.treePrototypes.Length}");
            var water = new SortedDictionary<string, int>();
            foreach (var r in Object.FindObjectsOfType<Renderer>())
                foreach (var m in r.sharedMaterials)
                    if (m != null && (m.shader.name.Contains("AQUAS") || m.shader.name.ToLowerInvariant().Contains("water")))
                    {
                        string k = $"'{Strip(r.name)}' shader='{m.shader.name}' y={r.bounds.center.y:F1} size={r.bounds.size.x:F0}x{r.bounds.size.z:F0}";
                        water[k] = (water.TryGetValue(k, out int n) ? n : 0) + 1;
                    }
            foreach (var kv in water) Plugin.Log.LogInfo($"[dev] inspect water {kv.Value} x {kv.Key}");
        }

        private static void Dump(Transform t, int depth)
        {
            var comps = new List<string>();
            foreach (var c in t.GetComponents<Component>())
                if (c != null && !(c is Transform)) comps.Add(c.GetType().Name);
            var ps = t.GetComponent<ParticleSystem>();
            string extra = ps != null ? $" mat='{t.GetComponent<ParticleSystemRenderer>()?.sharedMaterial?.name}'" : "";
            var l = t.GetComponent<Light>();
            if (l != null) extra += $" light {l.type} range={l.range} intensity={l.intensity} color={l.color}";
            Plugin.Log.LogInfo($"[dev] inspect {new string(' ', depth * 2)}{t.name} local={t.localPosition} [{string.Join(",", comps.ToArray())}]{extra}");
            if (depth < 4) foreach (Transform c in t) Dump(c, depth + 1);
        }

        private static string Strip(string name) => System.Text.RegularExpressions.Regex.Replace(name ?? string.Empty, @"\s*\(\d+\)$|\(Clone\)", string.Empty);

        // setweather clear|partlycloudy|overcast|rain|fog [now] | auto: hold a weather (blended, or at once with "now"), or resume the schedule.
        private static void SetWeather(string[] args)
        {
            var wx = GSOHDTextures.Weather.Instance;
            if (wx == null || args.Length < 2) { Plugin.Log.LogInfo("[dev] setweather: weather off or no pattern given"); return; }
            if (args[1].Equals("auto", System.StringComparison.OrdinalIgnoreCase)) { wx.Auto(); return; }
            if (!GSOHDTextures.Weather.TryParse(args[1], out var p)) { Plugin.Log.LogInfo($"[dev] setweather: unknown pattern {args[1]}"); return; }
            wx.Set(p, true, args.Length > 2 && args[2] == "now");
        }

        // settime <0-2400>: sets the time of day (1830 = 18:30).
        private static void SetTime(string[] args)
        {
            GSOHDTextures.Weather.Instance?.SetTime(float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture));
            Plugin.Log.LogInfo($"[dev] time set to {args[1]}");
        }

        // hdtex on|off: shows the HD textures or the originals, in the same session (for comparison screenshots).
        private static void HdTex(string[] args)
        {
            TextureReplacer.Instance?.SetPaused(args.Length > 1 && args[1] == "off");
        }

        // hdterrain: each active terrain's splat layers (texture and normal map) with their keys and what is shown now.
        private static void HdTerrain(string[] args)
        {
            foreach (var t in Terrain.activeTerrains)
            {
                var mat = t.materialTemplate;
                Plugin.Log.LogInfo($"[dev] terrain '{t.name}' material={(mat != null ? mat.name + " (" + mat.shader.name + ")" : t.materialType.ToString())}");
                int i = 0;
                foreach (var sp in t.terrainData.splatPrototypes)
                {
                    string Show(Texture2D tex) => tex == null ? "none" : $"{tex.name} {tex.width}x{tex.height} {tex.format}";
                    Plugin.Log.LogInfo($"[dev]   layer {i++}: tex={Show(sp.texture)} normal={Show(sp.normalMap)} tile={sp.tileSize} metallic={sp.metallic} smooth={sp.smoothness}");
                }
            }
        }

        // gfx on|off: switches enhanced lighting, like the toggle key.
        private static void Gfx(string[] args)
        {
            GSOHDTextures.Lighting.Instance?.Set(args.Length < 2 || args[1] != "off");
        }

        // lighting: the sky, ambient, sun, shadow and post-processing values that decide how the world looks.
        private static void Lighting(string[] args)
        {
            var log = Plugin.Log;
            var sky = TOD_Sky.Instance;
            if (sky != null)
            {
                log.LogInfo($"[dev] TOD ambient mode={sky.Ambient.Mode} saturation={sky.Ambient.Saturation} interval={sky.Ambient.UpdateInterval}; " +
                    $"day light={sky.Day.LightIntensity} shadow={sky.Day.ShadowStrength} ambientMul={sky.Day.AmbientMultiplier} reflMul={sky.Day.ReflectionMultiplier}; " +
                    $"night light={sky.Night.LightIntensity} ambientMul={sky.Night.AmbientMultiplier}; reflection mode={sky.Reflection.Mode}; " +
                    $"atmosphere brightness={sky.Atmosphere.Brightness} contrast={sky.Atmosphere.Contrast} fogginess={sky.Atmosphere.Fogginess}; hour={sky.Cycle.Hour:F1}");
                log.LogInfo($"[dev] TOD ambient color now={sky.AmbientColor}");
            }
            log.LogInfo($"[dev] RenderSettings ambient={RenderSettings.ambientMode} light={RenderSettings.ambientLight} intensity={RenderSettings.ambientIntensity}");
            log.LogInfo($"[dev] Quality shadows={QualitySettings.shadows} res={QualitySettings.shadowResolution} cascades={QualitySettings.shadowCascades} dist={QualitySettings.shadowDistance} proj={QualitySettings.shadowProjection} aniso={QualitySettings.anisotropicFiltering} lodBias={QualitySettings.lodBias} vSync={QualitySettings.vSyncCount} targetFps={Application.targetFrameRate}");
            foreach (var l in Object.FindObjectsOfType<Light>())
                if (l.type == LightType.Directional && l.enabled)
                    log.LogInfo($"[dev] sun '{l.name}' intensity={l.intensity} shadows={l.shadows} strength={l.shadowStrength} bias={l.shadowBias} normalBias={l.shadowNormalBias} res={l.shadowResolution}");
            var cam = Camera.main;
            var pp = cam != null ? cam.GetComponent<UnityEngine.PostProcessing.PostProcessingBehaviour>() : null;
            if (pp != null && pp.profile != null)
            {
                var p = pp.profile;
                var b = p.bloom.settings.bloom;
                var g = p.colorGrading.settings;
                var ao = p.ambientOcclusion.settings;
                log.LogInfo($"[dev] bloom on={p.bloom.enabled} intensity={b.intensity} threshold={b.threshold} knee={b.softKnee} radius={b.radius}");
                log.LogInfo($"[dev] grading on={p.colorGrading.enabled} tonemapper={g.tonemapping.tonemapper} exposure={g.basic.postExposure} temp={g.basic.temperature} tint={g.basic.tint} sat={g.basic.saturation} contrast={g.basic.contrast}");
                log.LogInfo($"[dev] ao on={p.ambientOcclusion.enabled} intensity={ao.intensity} radius={ao.radius} samples={ao.sampleCount} downsample={ao.downsampling} ambientOnly={ao.ambientOnly}");
                log.LogInfo($"[dev] aa on={p.antialiasing.enabled} method={p.antialiasing.settings.method}; eyeAdaptation={p.eyeAdaptation.enabled}; vignette={p.vignette.enabled}");
            }
            var scattering = cam != null ? cam.GetComponent<TOD_Scattering>() : null;
            if (scattering != null)
                log.LogInfo($"[dev] scattering on={scattering.enabled} density={scattering.GlobalDensity} falloff={scattering.HeightFalloff} zero={scattering.ZeroLevel}");
        }

        // hdplayer: what every texture property on the local player's meshes shows, and what it replaced.
        private static void HdPlayer(string[] args)
        {
            var replacer = TextureReplacer.Instance;
            if (replacer == null)
            {
                Plugin.Log.LogWarning("[dev] hdplayer: the replacer isn't running (disabled in config?)");
                return;
            }
            var seen = new HashSet<Material>();
            foreach (var renderer in Object.FindObjectsOfType<Renderer>())
            {
                if (renderer.gameObject.layer != LocalPlayerLayer || !renderer.enabled) continue;
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null || !seen.Add(mat)) continue;
                    Plugin.Log.LogInfo($"[dev] {renderer.name} / {mat.name} ({mat.shader.name})");
                    foreach (var line in replacer.Describe(mat))
                        Plugin.Log.LogInfo($"[dev]     {line}");
                }
            }
            Plugin.Log.LogInfo($"[dev] hdplayer: {seen.Count} materials");
        }

        // setuiscale <x>: set UI.Scale (saved to the cfg) and report the resulting factor.
        private static void SetUiScale(string[] args)
        {
            Plugin.UiUserScale.Value = float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
            UiScale.Recompute();
            Plugin.Log.LogInfo($"[dev] UI.Scale {Plugin.UiUserScale.Value} -> factor {UiScale.Factor:0.###}");
        }

        // openwindow <type> [tab] [scrollY]: open a classic window (2 inventory, 3 skills, 18 main menu; see
        // Script_WindowController). For the main menu, tab 1 is Video options (with Interface scale).
        private static void OpenWindow(string[] args)
        {
            var windows = Script_WindowController.instance;
            windows.OpenWindowType(int.Parse(args[1]));
            if (args.Length > 2 && args[1] == "18") windows.window_MainMenu.tab = int.Parse(args[2]);
            if (args.Length > 3 && args[1] == "18") windows.window_MainMenu.scrollPos = new Vector2(0f, float.Parse(args[3]));
            Plugin.Log.LogInfo($"[dev] opened window type {args[1]}{(args.Length > 2 ? " tab " + args[2] : "")}");
        }

        // hdui: the screen, the game's UI options, and every root canvas with how it scales.
        private static void HdUi(string[] args)
        {
            var o = Scr_Options.instance;
            Plugin.Log.LogInfo($"[dev] screen {Screen.width}x{Screen.height} dpi {Screen.dpi}");
            if (o != null)
                Plugin.Log.LogInfo($"[dev] options useLUI={o.useLUI} scaleGUI={o.scaleGUI} nativeSize={o.nativeSize} uiScaleMode={o.uiScaleMode} uiScaleFactor={o.uiScaleFactor}");
            if (Menucontroller.instance != null)
                Plugin.Log.LogInfo($"[dev] Menucontroller enabled={Menucontroller.instance.enabled} showGUI={Menucontroller.instance.showGUI}");
            if (Script_WindowController.instance != null)
                Plugin.Log.LogInfo($"[dev] Script_WindowController enabled={Script_WindowController.instance.enabled}");
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (!canvas.isRootCanvas || canvas.gameObject.scene.name == null) continue; // skip prefabs
                var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                var how = scaler == null ? "no scaler"
                    : $"{scaler.uiScaleMode} ref={scaler.referenceResolution} match={scaler.matchWidthOrHeight} constScale={scaler.scaleFactor}";
                var graphics = canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>(false).Length;
                Plugin.Log.LogInfo($"[dev] canvas '{canvas.name}' active={canvas.isActiveAndEnabled} {canvas.renderMode} scale={canvas.scaleFactor:0.###} {how} graphics={graphics}");
            }
        }
    }
}
