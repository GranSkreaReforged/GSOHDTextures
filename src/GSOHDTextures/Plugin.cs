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
        internal static ConfigEntry<bool> GfxEnabled;
        internal static ConfigEntry<KeyboardShortcut> GfxToggleKey;
        internal static ConfigEntry<bool> GfxSkyAmbient;
        internal static ConfigEntry<float> GfxAmbientTint;
        internal static ConfigEntry<float> GfxAmbientBrightness;
        internal static ConfigEntry<bool> GfxAmbientOcclusion;
        internal static ConfigEntry<float> GfxAoIntensity;
        internal static ConfigEntry<float> GfxExposure;
        internal static ConfigEntry<float> GfxContrast;
        internal static ConfigEntry<float> GfxSaturation;
        internal static ConfigEntry<float> GfxBloom;
        internal static ConfigEntry<float> GfxNightBrightness;
        internal static ConfigEntry<bool> GfxAnisotropic;
        internal static ConfigEntry<bool> WeatherEnabled;
        internal static ConfigEntry<float> WeatherDayMinutes;
        internal static ConfigEntry<string> WeatherMix;
        internal static ConfigEntry<float> WeatherMinMinutes;
        internal static ConfigEntry<float> WeatherMaxMinutes;
        internal static ConfigEntry<float> WeatherBlendSeconds;
        internal static ConfigEntry<float> WeatherMood;
        internal static ConfigEntry<bool> AmbEnabled;
        internal static ConfigEntry<bool> AmbLamps;
        internal static ConfigEntry<bool> AmbTorchFires;
        internal static ConfigEntry<bool> AmbFlicker;
        internal static ConfigEntry<bool> AmbWindows;
        internal static ConfigEntry<float> AmbWindowShare;
        internal static ConfigEntry<bool> AmbLightning;
        internal static ConfigEntry<bool> AmbFireflies;
        internal static ConfigEntry<bool> AmbDust;
        internal static ConfigEntry<bool> AmbMist;
        internal static ConfigEntry<bool> AmbLeaves;
        internal static ConfigEntry<bool> AmbSmoke;
        internal static ConfigEntry<float> AmbDensity;

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

            GfxEnabled = Config.Bind("Graphics", "EnhancedLighting", true, "Richer lighting than the original: sky-coloured ambient light, ambient occlusion, rebalanced exposure and contrast, sharper textures at an angle. The settings below fine-tune it.");
            GfxToggleKey = Config.Bind("Graphics", "ToggleKey", new KeyboardShortcut(KeyCode.F10), "Switch enhanced lighting on and off in-game, to compare with the original look.");
            GfxSkyAmbient = Config.Bind("Graphics", "SkyAmbient", true, "Ambient light takes the sky, horizon and ground colours from the time-of-day sky, instead of one flat grey.");
            GfxAmbientTint = Config.Bind("Graphics", "SkyAmbientTint", 0.5f, new ConfigDescription("How much of the sky's colour the ambient light takes (0 = neutral grey, 1 = full sky blue).", new AcceptableValueRange<float>(0f, 1.5f)));
            GfxAmbientBrightness = Config.Bind("Graphics", "SkyAmbientBrightness", 1.6f, new ConfigDescription("Brightness of the sky ambient light (lights shaded areas).", new AcceptableValueRange<float>(0.5f, 3f)));
            GfxAmbientOcclusion = Config.Bind("Graphics", "AmbientOcclusion", true, "Soft contact shadows in corners, under roofs and where objects meet the ground.");
            GfxAoIntensity = Config.Bind("Graphics", "AmbientOcclusionIntensity", 1f, new ConfigDescription("Strength of the ambient occlusion.", new AcceptableValueRange<float>(0f, 4f)));
            GfxExposure = Config.Bind("Graphics", "Exposure", 1.15f, new ConfigDescription("Overall brightness (the original uses 1.4, which washes colours out).", new AcceptableValueRange<float>(-2f, 3f)));
            GfxContrast = Config.Bind("Graphics", "Contrast", 1.1f, new ConfigDescription("Contrast (the original uses 0.85).", new AcceptableValueRange<float>(0.5f, 2f)));
            GfxSaturation = Config.Bind("Graphics", "Saturation", 1.1f, new ConfigDescription("Colour saturation (the original uses 1).", new AcceptableValueRange<float>(0f, 2f)));
            GfxNightBrightness = Config.Bind("Graphics", "NightBrightness", 2f, new ConfigDescription("Soft moonlight fill at night, so characters and the ground aren't silhouettes (0 = the original pitch-dark nights). Applies with SkyAmbient.", new AcceptableValueRange<float>(0f, 5f)));
            GfxBloom = Config.Bind("Graphics", "Bloom", 1f, new ConfigDescription("Glow around bright areas, relative to the original.", new AcceptableValueRange<float>(0f, 4f)));
            GfxAnisotropic = Config.Bind("Graphics", "AnisotropicFiltering", true, "Keep ground and wall textures sharp at shallow viewing angles.");

            WeatherEnabled = Config.Bind("Weather", "Enabled", true, "Changing weather (clear, cloudy, overcast, rain, fog) and a continuous day/night cycle, using the game's own clouds, rain, fog and time-of-day sky. Never snow.");
            WeatherDayMinutes = Config.Bind("Weather", "DayLengthMinutes", 15f, new ConfigDescription("Real minutes for a full day and night (the game's own speed is 40).", new AcceptableValueRange<float>(2f, 240f)));
            WeatherMix = Config.Bind("Weather", "Mix", "Clear=40,PartlyCloudy=30,Overcast=15,Rain=10,Fog=5", "How often each kind of weather comes up (relative weights). Fog is three times as likely early in the morning.");
            WeatherMinMinutes = Config.Bind("Weather", "MinMinutes", 4f, new ConfigDescription("Shortest time one kind of weather lasts.", new AcceptableValueRange<float>(0.5f, 120f)));
            WeatherMaxMinutes = Config.Bind("Weather", "MaxMinutes", 10f, new ConfigDescription("Longest time one kind of weather lasts.", new AcceptableValueRange<float>(0.5f, 240f)));
            WeatherBlendSeconds = Config.Bind("Weather", "BlendSeconds", 60f, new ConfigDescription("How long the change from one kind of weather to the next takes.", new AcceptableValueRange<float>(1f, 600f)));
            WeatherMood = Config.Bind("Weather", "Mood", 1f, new ConfigDescription("How strongly time of day and weather tint the picture with enhanced lighting on: cooler, darker nights, warm dawns and dusks, duller rain (0 = off).", new AcceptableValueRange<float>(0f, 2f)));

            AmbEnabled = Config.Bind("Ambience", "Enabled", true, "Extra lights and particles the game didn't have. The settings below switch each part.");
            AmbLamps = Config.Bind("Ambience", "Lamps", true, "Street lamps light up at dusk with a warm glow (the ones without a light get one).");
            AmbTorchFires = Config.Bind("Ambience", "TorchFires", true, "Standing torches without a fire get the same flame, light and smoke as the lit ones.");
            AmbFlicker = Config.Bind("Ambience", "Flicker", true, "Fires, torches and lamps flicker instead of glowing steadily.");
            AmbWindows = Config.Bind("Ambience", "Windows", true, "House and church windows glow warmly after dark.");
            AmbWindowShare = Config.Bind("Ambience", "LitWindowShare", 0.6f, new ConfigDescription("Share of buildings with lit windows at night.", new AcceptableValueRange<float>(0f, 1f)));
            AmbLightning = Config.Bind("Ambience", "Lightning", true, "Lightning flashes and thunder during heavy rain.");
            AmbFireflies = Config.Bind("Ambience", "Fireflies", true, "Fireflies drifting near the ground on dry nights.");
            AmbDust = Config.Bind("Ambience", "DustMotes", true, "Faint dust and pollen floating in the air on fair days.");
            AmbMist = Config.Bind("Ambience", "WaterMist", true, "Low mist drifting over the sea early in the morning and in fog.");
            AmbLeaves = Config.Bind("Ambience", "FallingLeaves", true, "Leaves drifting down from broadleaf trees.");
            AmbSmoke = Config.Bind("Ambience", "ChimneySmoke", true, "Smoke rising from chimneys.");
            AmbDensity = Config.Bind("Ambience", "ParticleDensity", 1f, new ConfigDescription("Amount of fireflies, dust, mist, leaves and smoke.", new AcceptableValueRange<float>(0f, 3f)));

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
            host.AddComponent<Lighting>();
            if (WeatherEnabled.Value) host.AddComponent<Weather>();
            if (AmbEnabled.Value) host.AddComponent<Ambience>();
            if (UiEnabled.Value)
            {
                GSOHDTextures.UiScale.Patch(harmony);
                UiSettings.Patch(harmony);
                host.AddComponent<UiController>();
            }

            Log.LogInfo($"{Name} {PluginInfo.BuildVersion} loaded{(PluginInfo.ReleaseBuild ? "" : " (dev build)")}. Textures: {(Enabled.Value ? TextureDir : "off")}, UI scaling: {(UiEnabled.Value ? "on" : "off")}, enhanced lighting: {(GfxEnabled.Value ? "on" : "off")} ({GfxToggleKey.Value} toggles), weather: {(WeatherEnabled.Value ? WeatherDayMinutes.Value + " min days" : "off")}.");
        }
    }
}
