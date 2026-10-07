using System.Text;
using UnityEngine;

namespace GSOHDTextures
{
    /// <summary>
    /// Identifies a game texture as "&lt;name&gt;__&lt;width&gt;x&lt;height&gt;", using the original size.
    /// Keys shared by different images (25 of them, e.g. ChainmailArms, Material.001_Base_Color) or
    /// differing only in case are marked by tools/textures/dump.py and never get a replacement.
    /// MUST stay identical to texture_key() in tools/textures/common.py.
    /// </summary>
    internal static class TextureKey
    {
        public static string Of(Texture2D tex) => Make(tex.name, tex.width, tex.height);

        public static string Make(string name, int width, int height) => $"{Sanitize(name)}__{width}x{height}";

        // Characters Windows forbids in file names, plus control characters, become '_'.
        public static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(c < 32 || "<>:\"/\\|?*".IndexOf(c) >= 0 ? '_' : c);
            return sb.ToString().Trim();
        }
    }
}
