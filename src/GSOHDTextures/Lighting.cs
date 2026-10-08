using UnityEngine;
using UnityEngine.PostProcessing;
using UnityEngine.Rendering;

namespace GSOHDTextures
{
    /// <summary>
    /// Optional lighting and image-quality tweaks on top of the game's own setup. The stock look is flat: a single
    /// grey ambient colour at full strength, +1.4 exposure with contrast lowered to 0.85, no ambient occlusion and
    /// no anisotropic filtering. This uses what the game already ships (Time of Day's sky-gradient ambient, the
    /// post-processing stack's ambient occlusion and grading) rather than new shaders.
    /// The values are re-applied whenever the game resets them (scene loads, the options menu), and the
    /// originals are kept so the toggle key can switch back for comparison.
    /// </summary>
    internal class Lighting : MonoBehaviour
    {
        private PostProcessingProfile profile;
        private ColorGradingModel.Settings originalGrading;
        private AmbientOcclusionModel.Settings originalAo;
        private BloomModel.Settings originalBloom;
        private bool originalAoEnabled;
        private TOD_Sky sky;
        private TOD_AmbientType originalAmbient;
        private float originalAmbientSaturation, originalDayAmbient, originalNightAmbient;
        private AnisotropicFiltering originalAniso;
        private bool on, applied, appliedUnderground;
        private float nextCheck;

        internal static Lighting Instance { get; private set; }

        private void Start()
        {
            Instance = this;
            on = Plugin.GfxEnabled.Value;
            originalAniso = QualitySettings.anisotropicFiltering;
        }

        private void Update()
        {
            if (Plugin.GfxToggleKey.Value.IsDown()) Set(!on);
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 1f;
            if (on) Apply();

            // Underground the game switches the sky off and drives a flat ambient colour itself; the sky
            // gradient mode would leave stale horizon/ground colours behind, so hand ambient back to it.
            var w = Script_WeatherController.instance;
            if (on && Plugin.GfxSkyAmbient.Value && w != null && w.isUnderground && RenderSettings.ambientMode != AmbientMode.Flat)
                RenderSettings.ambientMode = AmbientMode.Flat;
        }

        /// <summary>Turns enhanced lighting on or off now (toggle key, DevBridge "gfx"), remembering the choice.</summary>
        internal void Set(bool value)
        {
            on = value;
            Plugin.GfxEnabled.Value = on;
            Plugin.Log.LogInfo($"Enhanced lighting {(on ? "on" : "off")}.");
            if (Menucontroller.instance != null) Menucontroller.instance.screenNotification($"Enhanced lighting {(on ? "on" : "off")}");
            Revert();
            if (on) Apply();
            nextCheck = Time.unscaledTime + 1f;
        }

        private void Apply()
        {
            var cam = Camera.main;
            var pp = cam != null ? cam.GetComponent<PostProcessingBehaviour>() : null;
            if (pp != null && pp.profile != null && pp.profile != profile)
            {
                Revert();   // a different profile: put the old one back first
                profile = pp.profile;
                originalGrading = profile.colorGrading.settings;
                originalAo = profile.ambientOcclusion.settings;
                originalAoEnabled = profile.ambientOcclusion.enabled;
                originalBloom = profile.bloom.settings;
                applied = false;
            }
            // Underground the game switches the sky off and lights caves with torches; the daylight grading
            // (lower exposure, more contrast) makes them nearly black, so caves keep the original exposure.
            var s = TOD_Sky.Instance;
            var weather = Script_WeatherController.instance;
            bool underground = weather != null && weather.isUnderground;
            if (profile != null && (!applied || underground != appliedUnderground))
            {
                var g = originalGrading;
                g.basic.postExposure = underground ? originalGrading.basic.postExposure : Plugin.GfxExposure.Value;
                g.basic.contrast = underground ? 1f : Plugin.GfxContrast.Value;
                g.basic.saturation = Plugin.GfxSaturation.Value;
                profile.colorGrading.settings = g;
                appliedUnderground = underground;

                if (Plugin.GfxAmbientOcclusion.Value)
                {
                    var ao = originalAo;
                    ao.intensity = Plugin.GfxAoIntensity.Value;
                    ao.radius = 0.4f;
                    ao.sampleCount = AmbientOcclusionModel.SampleCount.Medium;
                    ao.downsampling = false;
                    ao.ambientOnly = true;   // deferred rendering: darken only the ambient term, not lit surfaces
                    profile.ambientOcclusion.settings = ao;
                    profile.ambientOcclusion.enabled = true;
                }

                var b = originalBloom;
                b.bloom.intensity = originalBloom.bloom.intensity * Plugin.GfxBloom.Value;
                profile.bloom.settings = b;
                applied = true;
            }

            if (s != null && s != sky)
            {
                sky = s;
                originalAmbient = s.Ambient.Mode;
                originalAmbientSaturation = s.Ambient.Saturation;
                originalDayAmbient = s.Day.AmbientMultiplier;
                originalNightAmbient = s.Night.AmbientMultiplier;
            }
            if (sky != null && Plugin.GfxSkyAmbient.Value && sky.Ambient.Mode != TOD_AmbientType.Gradient)
            {
                // Sky, horizon and ground colours from the time-of-day sky instead of one flat grey. The raw sky
                // colours are a deep blue, so they're desaturated and brightened to read as daylight, not dusk.
                sky.Ambient.Mode = TOD_AmbientType.Gradient;
                sky.Ambient.Saturation = Plugin.GfxAmbientTint.Value;
                sky.Day.AmbientMultiplier = originalDayAmbient * Plugin.GfxAmbientBrightness.Value;
                sky.Night.AmbientMultiplier = originalNightAmbient * Plugin.GfxAmbientBrightness.Value;
            }

            if (Plugin.GfxAnisotropic.Value) QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
        }

        private void Revert()
        {
            if (profile != null && applied)
            {
                profile.colorGrading.settings = originalGrading;
                profile.ambientOcclusion.settings = originalAo;
                profile.ambientOcclusion.enabled = originalAoEnabled;
                profile.bloom.settings = originalBloom;
            }
            applied = false;
            if (sky != null)
            {
                sky.Ambient.Mode = originalAmbient;
                sky.Ambient.Saturation = originalAmbientSaturation;
                sky.Day.AmbientMultiplier = originalDayAmbient;
                sky.Night.AmbientMultiplier = originalNightAmbient;
            }
            QualitySettings.anisotropicFiltering = originalAniso;
        }

        private void OnDestroy() => Revert();
    }
}
