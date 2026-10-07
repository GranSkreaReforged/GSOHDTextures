using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GSOHDTextures
{
    /// <summary>
    /// Keeps <see cref="UiScale.Factor"/> current, handles the scale hotkeys, shows the new scale briefly,
    /// and applies the user's scale to the game's canvases (minimap, zone name and the newer LUI windows),
    /// which already follow the resolution through their CanvasScaler.
    /// </summary>
    internal class UiController : MonoBehaviour
    {
        private const float Step = 0.05f;
        private const float ToastSeconds = 2f;

        private struct Original
        {
            public Vector2 ReferenceResolution;
            public float ScaleFactor;
        }

        private readonly Dictionary<CanvasScaler, Original> scalers = new Dictionary<CanvasScaler, Original>();
        private float appliedUserScale = -1f, appliedFactor = -1f, nextScan, toastUntil;
        private GUIStyle toastStyle;

        private void Update()
        {
            UiScale.Recompute();

            if (Plugin.UiEnabled.Value)
            {
                if (Plugin.UiScaleUpKey.Value.IsDown()) SetUserScale(Plugin.UiUserScale.Value + Step);
                if (Plugin.UiScaleDownKey.Value.IsDown()) SetUserScale(Plugin.UiUserScale.Value - Step);
                if (Plugin.UiScaleResetKey.Value.IsDown()) SetUserScale(1f);
            }

            // New canvases appear with scenes and windows; changed scales apply to all of them at once.
            var user = Plugin.UiEnabled.Value ? Plugin.UiUserScale.Value : 1f;
            if (Time.unscaledTime >= nextScan || user != appliedUserScale || UiScale.Factor != appliedFactor)
            {
                nextScan = Time.unscaledTime + 2f;
                ApplyToCanvases(user, user != appliedUserScale || UiScale.Factor != appliedFactor);
                appliedUserScale = user;
                appliedFactor = UiScale.Factor;
            }
        }

        private void SetUserScale(float value)
        {
            Plugin.UiUserScale.Value = Mathf.Clamp(Mathf.Round(value / Step) * Step, 0.5f, 3f); // saved to the cfg
            UiScale.Recompute();
            toastUntil = Time.unscaledTime + ToastSeconds;
        }

        private void ApplyToCanvases(float user, bool changed)
        {
            foreach (var scaler in FindObjectsOfType<CanvasScaler>())
            {
                if (!scalers.TryGetValue(scaler, out var original))
                {
                    original = new Original { ReferenceResolution = scaler.referenceResolution, ScaleFactor = scaler.scaleFactor };
                    scalers[scaler] = original;
                }
                else if (!changed) continue;

                switch (scaler.uiScaleMode)
                {
                    case CanvasScaler.ScaleMode.ScaleWithScreenSize:
                        // Already follows the resolution; only the user's preference is added.
                        scaler.referenceResolution = original.ReferenceResolution / user;
                        break;
                    case CanvasScaler.ScaleMode.ConstantPixelSize:
                        scaler.scaleFactor = original.ScaleFactor * UiScale.Factor;
                        break;
                }
            }
            // Destroyed canvases leave null keys behind.
            if (changed)
            {
                var dead = new List<CanvasScaler>();
                foreach (var s in scalers.Keys) if (s == null) dead.Add(s);
                foreach (var s in dead) scalers.Remove(s);
            }
        }

        private void OnGUI()
        {
            if (Time.unscaledTime >= toastUntil) return;
            if (toastStyle == null)
                toastStyle = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            GUI.matrix = Matrix4x4.Scale(new Vector3(UiScale.Factor, UiScale.Factor, 1f));
            var w = Screen.width / UiScale.Factor;
            var text = $"UI scale {Plugin.UiUserScale.Value * 100f:0}%  ({UiScale.Factor * 100f:0}% of native pixels)";
            GUI.Box(new Rect(w / 2f - 190f, 60f, 380f, 36f), text, toastStyle);
        }
    }
}
