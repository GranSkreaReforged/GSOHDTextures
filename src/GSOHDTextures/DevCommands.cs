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
    }
}
