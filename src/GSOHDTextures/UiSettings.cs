using HarmonyLib;
using UnityEngine;

namespace GSOHDTextures
{
    /// <summary>
    /// Adds an "Interface scale" slider to the game's Main menu → Video options tab
    /// (Script_WindowController.WindowMainMenu). That tab leaves an empty two-row gap after
    /// "Texture quality", so a postfix on the game's own slider helper draws ours there, in the same
    /// style and inside the same scroll view.
    /// </summary>
    internal static class UiSettings
    {
        private const string Anchor = "Texture quality";
        private const float Min = 0.5f, Max = 2f, Step = 0.05f;

        // While the slider is dragged the value is only pending: applying it would resize the window
        // under the mouse. UiController applies it when the button is released.
        internal static float? Pending;

        internal static void Patch(Harmony harmony)
        {
            var drawSlider = AccessTools.Method(typeof(Menucontroller), nameof(Menucontroller.DrawSlider));
            harmony.Patch(drawSlider, postfix: new HarmonyMethod(typeof(UiSettings), nameof(AfterSlider)));
        }

        private static bool drawing;

        private static void AfterSlider(Menucontroller __instance, Rect rect, string t)
        {
            if (drawing || t == null || !t.StartsWith(Anchor)) return;
            drawing = true;
            try
            {
                var current = Pending ?? Plugin.UiUserScale.Value;
                // Short like the game's other labels; the on-screen readout gives the effective size.
                var label = $"Interface scale: {current * 100f:0}%";
                // A cfg value outside the slider's range is shown at the end stop but only changed by dragging.
                var shown = Mathf.Clamp(current, Min, Max);
                var value = __instance.DrawSlider(new Rect(rect.x, rect.y + 50f, rect.width, rect.height), label, shown, Min, Max);
                value = Mathf.Round(value / Step) * Step;
                if (Mathf.Abs(value - shown) > Step / 2f) Pending = value;
            }
            finally { drawing = false; }
        }
    }
}
