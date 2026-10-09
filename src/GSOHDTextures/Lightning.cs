using UnityEngine;

namespace GSOHDTextures
{
    /// <summary>
    /// Lightning in heavy rain: a bolt (the game's own "Lightning strike" effect, GFX id 47) somewhere out in the
    /// landscape, a double flash from a directional light, and thunder after the time sound takes to arrive. The
    /// game has no thunder sound, so the thunder is synthesised: a crack, then a low rolling rumble.
    /// </summary>
    internal class Lightning
    {
        private const int BoltEffect = 47;
        private const float HeavyRain = 92f;   // cloudiness from which it storms (rain starts at 80)

        private readonly AudioClip[] thunder = new AudioClip[3];
        private GameObject flashObject;
        private Light flash;
        private AudioSource audio;
        private float nextStrike = -1f, flashStart = -1f, flashPower, thunderAt = -1f, thunderVolume;
        private int thunderClip;

        internal Lightning()
        {
            for (int i = 0; i < thunder.Length; i++) thunder[i] = MakeThunder(i * 7919 + 13);
        }

        private void Ensure()
        {
            if (flashObject != null) return;
            flashObject = new GameObject("HD Lightning");
            Object.DontDestroyOnLoad(flashObject);
            flashObject.hideFlags = HideFlags.HideAndDontSave;
            flash = flashObject.AddComponent<Light>();
            flash.type = LightType.Directional;
            flash.color = new Color(0.8f, 0.86f, 1f);
            flash.intensity = 0f;
            flash.shadows = LightShadows.None;
            flash.enabled = false;
            audio = flashObject.AddComponent<AudioSource>();
            audio.spatialBlend = 0f;
            audio.playOnAwake = false;
            if (Scr_AudioHandler.instance != null) audio.outputAudioMixerGroup = Scr_AudioHandler.instance.audioMixerGroup_Sounds;
        }

        internal void Update()
        {
            if (Ambience.Outdoors && Ambience.Cloudiness >= HeavyRain && Ambience.Player != null)
            {
                if (nextStrike < 0f) nextStrike = Time.time + Random.Range(8f, 20f);
                else if (Time.time >= nextStrike)
                {
                    Strike();
                    nextStrike = Time.time + Random.Range(12f, 40f);
                }
            }
            else nextStrike = -1f;
            Animate();
        }

        /// <summary>One strike now (also the DevBridge "lightning" command).</summary>
        internal void Strike()
        {
            var player = Ambience.Player;
            if (player == null) return;
            Ensure();
            float distance = Random.Range(120f, 450f);
            float angle = Random.Range(0f, 360f);
            var dir = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            var pos = player.position + dir * distance;
            var terrain = Terrain.activeTerrain;
            pos.y = terrain != null ? terrain.SampleHeight(pos) + terrain.transform.position.y : player.position.y;
            if (EffectController.instance != null) EffectController.instance.playEffect(BoltEffect, pos);

            // Light from the bolt's side of the sky, brighter the closer it is.
            flashObject.transform.rotation = Quaternion.LookRotation(-dir + Vector3.down * 1.2f);
            flashPower = Mathf.Lerp(1.6f, 0.5f, (distance - 120f) / 330f);
            flashStart = Time.time;
            flash.enabled = true;

            thunderAt = Time.time + distance / 343f;
            thunderVolume = Mathf.Lerp(1f, 0.45f, (distance - 120f) / 330f);
            thunderClip = Random.Range(0, thunder.Length);
            Plugin.Log.LogInfo($"[ambience] lightning {distance:F0} m away, thunder in {distance / 343f:F1} s");
        }

        // Two quick flashes, then a fade.
        private void Animate()
        {
            if (flashStart >= 0f)
            {
                float t = Time.time - flashStart;
                float k = t < 0.06f ? t / 0.06f : t < 0.14f ? 0.25f : t < 0.2f ? 1f : Mathf.Max(0f, 1f - (t - 0.2f) / 0.35f);
                flash.intensity = flashPower * k;
                if (t > 0.6f)
                {
                    flash.intensity = 0f;
                    flash.enabled = false;
                    flashStart = -1f;
                }
            }
            if (thunderAt >= 0f && Time.time >= thunderAt)
            {
                thunderAt = -1f;
                audio.pitch = Random.Range(0.85f, 1.1f);
                audio.PlayOneShot(thunder[thunderClip], thunderVolume);
            }
        }

        /// <summary>A few seconds of thunder: a short noisy crack, then brown-noise rumble rolling in bursts and fading.</summary>
        private static AudioClip MakeThunder(int seed)
        {
            const int rate = 22050;
            float seconds = 7f;
            int n = (int)(rate * seconds);
            var data = new float[n];
            var rng = new System.Random(seed);
            float brown = 0f, low = 0f, crackLow = 0f;
            // Rolling: a few bumps of loudness at random times.
            int rolls = 3 + rng.Next(4);
            var rollAt = new float[rolls];
            var rollAmp = new float[rolls];
            for (int i = 0; i < rolls; i++) { rollAt[i] = 0.3f + (float)rng.NextDouble() * 3.5f; rollAmp[i] = 0.4f + (float)rng.NextDouble() * 0.6f; }
            float peak = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                float white = (float)rng.NextDouble() * 2f - 1f;
                brown = Mathf.Clamp(brown + white * 0.02f, -1f, 1f) * 0.999f;
                low += (brown - low) * 0.08f;                         // low-pass: the rumble
                crackLow += (white - crackLow) * 0.35f;               // brighter noise for the crack
                float env = Mathf.Exp(-t * 0.55f) * 0.5f;
                for (int r = 0; r < rolls; r++)
                {
                    float d = (t - rollAt[r]) / 0.45f;
                    env += rollAmp[r] * Mathf.Exp(-d * d);
                }
                float crack = t < 0.25f ? Mathf.Exp(-t * 18f) : 0f;
                float fadeOut = t > seconds - 1.5f ? (seconds - t) / 1.5f : 1f;
                data[i] = (low * env * 3f + crackLow * crack * 0.8f) * fadeOut;
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            if (peak > 0f) for (int i = 0; i < n; i++) data[i] *= 0.9f / peak;
            var clip = AudioClip.Create("HD Thunder " + seed, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
