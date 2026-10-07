using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace GSOHDTextures
{
    /// <summary>
    /// The classic interface draws its icons and HUD images with GUI.DrawTexture, which never goes
    /// through a material, so <see cref="TextureReplacer"/>'s scan can't see them. These prefixes hand
    /// GUI.DrawTexture the HD version instead. It draws the whole texture into a rectangle (or a
    /// normalised UV rectangle), so a larger texture changes nothing but sharpness.
    /// </summary>
    internal static class UiTextures
    {
        internal static void Patch(Harmony harmony)
        {
            var prefix = new HarmonyMethod(typeof(UiTextures), nameof(Prefix));
            var count = 0;
            foreach (var m in typeof(GUI).GetMethods().Where(m => m.Name == "DrawTexture" || m.Name == "DrawTextureWithTexCoords"))
            {
                var p = m.GetParameters();
                if (p.Length < 2 || p[1].ParameterType != typeof(Texture) || p[1].Name != "image") continue;
                harmony.Patch(m, prefix: prefix);
                count++;
            }
            Plugin.Log.LogInfo($"UI textures: patched {count} GUI.DrawTexture overloads.");
        }

        private static void Prefix(ref Texture image)
        {
            var replacer = TextureReplacer.Instance;
            if (replacer == null || image == null) return;
            var hd = replacer.UiReplacement(image);
            if (hd != null) image = hd;
        }
    }
}
