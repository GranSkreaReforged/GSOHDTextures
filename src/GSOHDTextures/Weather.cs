using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace GSOHDTextures
{
    /// <summary>
    /// Changing weather and a continuous day/night cycle. The game already has the whole weather system
    /// (Script_WeatherController): cloudiness drives the time-of-day sky's clouds and the overcast layer, rain
    /// particles and audio above 80, and fog distance; fogginess hazes the atmosphere; time advances by timeSpeed.
    /// The server used to drive those numbers. This drives them locally instead: a clock at the configured day
    /// length, and weather patterns blended in one after another. Temperature stays above zero, so never snow.
    /// </summary>
    internal class Weather : MonoBehaviour
    {
        internal enum Pattern { Clear, PartlyCloudy, Overcast, Rain, Fog }

        private struct Look
        {
            public float cloud, fogginess, fog, wind;   // fog: 0..1 towards the close fog distance
        }

        // What each pattern looks like in the game's own terms (rain needs cloudiness above 80).
        private static Look LookOf(Pattern p)
        {
            switch (p)
            {
                case Pattern.Clear: return new Look { cloud = 8f, fogginess = 0f, fog = 0f, wind = 0.3f };
                case Pattern.PartlyCloudy: return new Look { cloud = 40f, fogginess = 0.05f, fog = 0f, wind = 0.5f };
                case Pattern.Overcast: return new Look { cloud = 72f, fogginess = 0.15f, fog = 0.15f, wind = 0.8f };
                case Pattern.Rain: return new Look { cloud = 96f, fogginess = 0.3f, fog = 0.35f, wind = 1.5f };
                default: return new Look { cloud = 45f, fogginess = 0.6f, fog = 1f, wind = 0.1f };   // Fog
            }
        }

        private const float CloseFogEnd = 110f;   // metres, with fog at full strength
        private const float MinTemperature = 5f;  // the game snows at 0 or below

        internal static Weather Instance { get; private set; }

        private Script_WeatherController controller;
        private float clock = -1f, controllerSince;
        private bool sceneHasCustomFogEnd;

        private Pattern pattern = Pattern.Clear;
        private bool forced;
        private float nextChange;
        private Look current, target;
        private readonly System.Random random = new System.Random();

        internal Pattern Current => pattern;
        internal bool Held => forced;
        internal int SecondsToChange => Mathf.Max(0, Mathf.RoundToInt(nextChange - Time.time));
        internal float Hour => clock < 0f ? 12f : clock / 100f;
        internal float Cloudiness => current.cloud;
        internal float FogAmount => current.fog;
        internal bool Underground => controller != null && controller.isUnderground;

        private void Start()
        {
            Instance = this;
            current = target = LookOf(pattern);
            nextChange = Time.time + 60f;   // a minute of the starting weather, then the schedule takes over
        }

        private void Update()
        {
            var w = Script_WeatherController.instance;
            if (w == null || w.todSky == null) return;
            if (w != controller) Adopt(w);
            TickClock(w);
            TickPattern();
            Blend();
            ApplyTo(w);
        }

        // A new controller comes with each world scene. Keep our clock running through it.
        private void Adopt(Script_WeatherController w)
        {
            controller = w;
            controllerSince = Time.time;
            sceneHasCustomFogEnd = w.hasCustomFogEndDistance;
            if (clock < 0f) clock = w.time;
            else w.time = clock;
            Plugin.Log.LogInfo($"[weather] scene weather controller: time {clock:F0}, {pattern}, custom fog end: {sceneHasCustomFogEnd}");
        }

        private static float Shortest(float a, float b)
        {
            float d = (a - b) % 2400f;
            if (d > 1200f) d -= 2400f;
            if (d < -1200f) d += 2400f;
            return d;
        }

        private void TickClock(Script_WeatherController w)
        {
            w.timeSpeed = 2400f / (Plugin.WeatherDayMinutes.Value * 60f);
            float drift = Shortest(w.time, clock);
            if (Mathf.Abs(drift) > 50f)
            {
                // A jump the clock didn't make. Right after a scene load it's the server's start time: ignore it.
                // Later it's deliberate (a /time command, the login time): adopt it.
                if (Time.time - controllerSince < 15f) w.time = clock;
                else
                {
                    Plugin.Log.LogInfo($"[weather] time set to {w.time:F0} (was {clock:F0})");
                    clock = w.time;
                }
            }
            else clock = w.time;
        }

        private void TickPattern()
        {
            if (forced || Time.time < nextChange) return;
            Set(Pick(), false);
        }

        private Pattern Pick()
        {
            var weights = new Dictionary<Pattern, float>();
            foreach (string part in (Plugin.WeatherMix.Value ?? string.Empty).Split(','))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && TryParse(kv[0].Trim(), out var p) &&
                    float.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float wt) && wt > 0f)
                    weights[p] = wt;
            }
            if (weights.Count == 0) return Pattern.Clear;
            float h = Hour;
            if (weights.ContainsKey(Pattern.Fog) && h >= 4f && h < 8f) weights[Pattern.Fog] *= 3f;   // misty mornings

            float total = 0f;
            foreach (var wt in weights.Values) total += wt;
            float roll = (float)random.NextDouble() * total;
            foreach (var kv in weights)
            {
                roll -= kv.Value;
                if (roll <= 0f) return kv.Key;
            }
            return Pattern.Clear;
        }

        internal static bool TryParse(string s, out Pattern p)
        {
            foreach (Pattern v in Enum.GetValues(typeof(Pattern)))
                if (string.Equals(v.ToString(), s, StringComparison.OrdinalIgnoreCase)) { p = v; return true; }
            p = Pattern.Clear;
            return false;
        }

        /// <summary>Switch to a pattern (blended). <paramref name="hold"/> keeps it until Auto() (DevBridge setweather).</summary>
        internal void Set(Pattern p, bool hold, bool instant = false)
        {
            pattern = p;
            forced = hold;
            target = LookOf(p);
            if (instant) current = target;
            float min = Plugin.WeatherMinMinutes.Value, max = Mathf.Max(min, Plugin.WeatherMaxMinutes.Value);
            nextChange = Time.time + 60f * (min + (float)random.NextDouble() * (max - min));
            Plugin.Log.LogInfo($"[weather] {p}{(hold ? " (held)" : "")}, next change in {Mathf.RoundToInt(nextChange - Time.time)}s");
        }

        internal void Auto()
        {
            forced = false;
            nextChange = Time.time;
        }

        internal void SetTime(float time)
        {
            clock = Mathf.Repeat(time, 2400f);
            if (controller != null) controller.time = clock;
        }

        private void Blend()
        {
            float k = Mathf.Clamp01(Time.deltaTime / Mathf.Max(1f, Plugin.WeatherBlendSeconds.Value));
            current.cloud = Mathf.MoveTowards(current.cloud, target.cloud, k * 100f);
            current.fogginess = Mathf.MoveTowards(current.fogginess, target.fogginess, k);
            current.fog = Mathf.MoveTowards(current.fog, target.fog, k);
            current.wind = Mathf.MoveTowards(current.wind, target.wind, k * 2f);
        }

        private TOD_Sky sky;
        private float skyLight, skyShadow;

        // The sun keeps full strength and hard shadows whatever the clouds; under heavy cloud dim it and soften them.
        private void Overcast(TOD_Sky s, float cloud)
        {
            if (s != sky)
            {
                if (sky != null) { sky.Day.LightIntensity = skyLight; sky.Day.ShadowStrength = skyShadow; }
                sky = s;
                skyLight = s.Day.LightIntensity;
                skyShadow = s.Day.ShadowStrength;
            }
            float cover = Mathf.Clamp01((cloud - 50f) / 45f);
            s.Day.LightIntensity = skyLight * (1f - 0.45f * cover);
            s.Day.ShadowStrength = skyShadow * (1f - 0.7f * cover);
        }

        private void ApplyTo(Script_WeatherController w)
        {
            if (w.temperature < MinTemperature) w.temperature = MinTemperature;
            if (w.isUnderground) return;   // caves: the game hides the sky and sets its own fog
            Overcast(w.todSky, current.cloud);
            w.cloudiness = current.cloud;
            w.fogginess = current.fogginess;
            w.windSpeed = current.wind;
            if (!sceneHasCustomFogEnd)
            {
                // The game fades the fog end towards this itself; it otherwise sits at 80% of the view distance.
                var cam = CameraController.instance != null ? CameraController.instance.cam : null;
                float far = cam != null ? cam.farClipPlane * 0.8f : 400f;
                w.hasCustomFogEndDistance = current.fog > 0.01f;
                w.customFogEndDistance = Mathf.RoundToInt(Mathf.Lerp(far, CloseFogEnd, current.fog));
            }
        }

        private void OnDestroy()
        {
            if (controller != null && !sceneHasCustomFogEnd) controller.hasCustomFogEndDistance = false;
            if (sky != null) { sky.Day.LightIntensity = skyLight; sky.Day.ShadowStrength = skyShadow; }
        }

        /// <summary>
        /// Colour-grading offsets for Lighting: cooler and darker at night, warm at dawn and dusk, duller and darker
        /// under rain clouds, flatter in fog. Values are reconstructions tuned by eye, scaled by Weather.Mood.
        /// </summary>
        internal void Mood(out float exposure, out float temperature, out float saturation, out float contrast)
        {
            exposure = temperature = 0f;
            saturation = contrast = 1f;
            if (controller == null || controller.isUnderground) return;
            float h = Hour, m = Plugin.WeatherMood.Value;
            float night = h >= 21f || h < 4f ? 1f : h >= 19f ? (h - 19f) / 2f : h < 6f ? 1f - (h - 4f) / 2f : 0f;
            float golden = Mathf.Max(Peak(h, 6.5f, 1.5f), Peak(h, 18.5f, 1.5f));
            float wet = Mathf.Clamp01((current.cloud - 60f) / 35f);
            // The time-of-day sky already makes nights dark and blue; only nudge them, so they stay playable.
            exposure = m * (0.15f * night - 0.2f * wet);
            temperature = m * (-4f * night + 12f * golden * (1f - wet));
            saturation = 1f - m * (0.1f * night + 0.25f * wet);
            contrast = 1f - m * 0.1f * current.fog;
        }

        private static float Peak(float h, float centre, float halfWidth) => Mathf.Clamp01(1f - Mathf.Abs(h - centre) / halfWidth);
    }
}
