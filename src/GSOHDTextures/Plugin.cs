using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
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
        internal static ConfigEntry<bool> UiEnabled;
        internal static ConfigEntry<bool> UiAutoScale;
        internal static ConfigEntry<float> UiUserScale;
        internal static ConfigEntry<bool> UiHdTextures;
        internal static ConfigEntry<KeyboardShortcut> UiScaleUpKey;
        internal static ConfigEntry<KeyboardShortcut> UiScaleDownKey;
        internal static ConfigEntry<KeyboardShortcut> UiScaleResetKey;

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

            UiEnabled = Config.Bind("UI", "Enabled", true, "Scale the game's menus, windows and HUD for high-resolution screens. Needs a restart to turn on or off completely.");
            UiAutoScale = Config.Bind("UI", "AutoScale", true, "Grow the classic interface with the screen height, so it looks as it was designed on a 1080p screen (1440p = 133%, 4K = 200%). Off: native pixels times Scale.");
            UiUserScale = Config.Bind("UI", "Scale", 1f, new ConfigDescription("Your preference on top of AutoScale, for larger or smaller text and menus. Also set in-game with the keys below.", new AcceptableValueRange<float>(0.5f, 3f)));
            UiHdTextures = Config.Bind("UI", "HdTextures", true, "Draw icons and other interface images from the texture pack, so the scaled interface stays sharp.");
            UiScaleUpKey = Config.Bind("UI", "ScaleUpKey", new KeyboardShortcut(KeyCode.Equals, KeyCode.LeftControl), "Make the interface 5% larger.");
            UiScaleDownKey = Config.Bind("UI", "ScaleDownKey", new KeyboardShortcut(KeyCode.Minus, KeyCode.LeftControl), "Make the interface 5% smaller.");
            UiScaleResetKey = Config.Bind("UI", "ScaleResetKey", new KeyboardShortcut(KeyCode.Alpha0, KeyCode.LeftControl), "Reset Scale to 100%.");

            TextureDir = Path.IsPathRooted(folder.Value) ? folder.Value : Path.Combine(Path.GetDirectoryName(Info.Location), folder.Value);

            var host = new GameObject("GSOHDTextures");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            var harmony = new Harmony(Guid);

            if (Enabled.Value)
            {
                host.AddComponent<TextureReplacer>();
                if (UiHdTextures.Value) UiTextures.Patch(harmony);
            }
            if (UiEnabled.Value)
            {
                GSOHDTextures.UiScale.Patch(harmony);
                UiSettings.Patch(harmony);
                host.AddComponent<UiController>();
            }

            Log.LogInfo($"{Name} {Version} loaded. Textures: {(Enabled.Value ? TextureDir : "off")}, UI scaling: {(UiEnabled.Value ? "on" : "off")}.");
        }
    }
}
