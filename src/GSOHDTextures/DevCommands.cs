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
