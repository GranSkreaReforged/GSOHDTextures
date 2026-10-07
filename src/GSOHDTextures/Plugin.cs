using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace GSOHDTextures
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "gso.hdtextures";
        public const string Name = "GSO HD Textures";
        public const string Version = PluginInfo.Version;

        internal static ManualLogSource Log;
        internal static string TextureDir;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> CompressTextures;
        internal static ConfigEntry<float> ScanInterval;
        internal static ConfigEntry<string> ExtraProperties;
        internal static ConfigEntry<KeyboardShortcut> ReloadKey;
        internal static ConfigEntry<bool> LogReplacements;
        internal static ConfigEntry<bool> RecordSeenTextures;

        private void Awake()
        {
            Log = Logger;

            Enabled = Config.Bind("General", "Enabled", true, "Replace game textures with the ones in TextureFolder.");
            var folder = Config.Bind("General", "TextureFolder", "textures", "Folder of replacement textures, relative to this plugin's folder (or absolute).");
            CompressTextures = Config.Bind("General", "CompressTextures", true, "Compress hand-made .png replacements to DXT1/DXT5 after loading (the .dds files pack.ps1 builds are already compressed). Uses about a quarter of the VRAM but loads more slowly.");
            ScanInterval = Config.Bind("General", "ScanInterval", 2f, "Seconds between scans for newly loaded materials (NPCs, equipment, effects). 0 = scan only on scene load.");
            ExtraProperties = Config.Bind("Advanced", "ExtraTextureProperties", "", "Comma-separated shader texture properties to check besides the built-in list (e.g. _Ramp,_MaskTex). Suffix with ! for linear data such as normal maps.");
            ReloadKey = Config.Bind("Debug", "ReloadKey", new KeyboardShortcut(KeyCode.F9), "Reload the texture folder and re-apply everything (for iterating on textures in-game).");
            LogReplacements = Config.Bind("Debug", "LogReplacements", false, "Log every texture as it is replaced.");
            RecordSeenTextures = Config.Bind("Debug", "RecordSeenTextures", false, "Append the key of every texture the game uses to BepInEx/GSOHDTextures-seen.txt (with or without a replacement). Feed it to pack.ps1 -OnlyList to build a pack of what you actually see.");

            TextureDir = Path.IsPathRooted(folder.Value) ? folder.Value : Path.Combine(Path.GetDirectoryName(Info.Location), folder.Value);

            if (!Enabled.Value)
            {
                Log.LogInfo($"{Name} {Version} disabled in config.");
                return;
            }

            var host = new GameObject("GSOHDTextures");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            host.AddComponent<TextureReplacer>();

            Log.LogInfo($"{Name} {Version} loaded. Textures: {TextureDir}");
        }
    }
}
