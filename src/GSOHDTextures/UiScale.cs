using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace GSOHDTextures
{
    /// <summary>
    /// Scales the game's classic IMGUI interface (Menucontroller, Script_WindowController: the HUD, chat
    /// and every window), which is drawn at 1:1 pixels and gets tiny on high-resolution screens.
    ///
    /// Each game OnGUI runs under a GUI.matrix scale. Its layout code positions everything from
    /// Screen.width/height and Input.mousePosition, so while it runs those report a "virtual" screen of
    /// Screen / Factor, and screen projections are converted between the two (nameplates stay over heads,
    /// world clicks stay exact). The calls are rewritten by Harmony transpilers and only change behaviour
    /// inside a scaled OnGUI (<see cref="depth"/>), or always in classes that only deal in GUI space.
    /// </summary>
    internal static class UiScale
    {
        // The classic UI was laid out for 1080p screens.
        private const float DesignHeight = 1080f;

        /// <summary>Factor applied to the IMGUI interface: user scale x (screen height / 1080 if automatic).</summary>
        internal static float Factor { get; private set; } = 1f;

        private static int depth;
        private static bool Active => depth > 0;

        // Classes that only ever work in GUI coordinates, including outside OnGUI (window placement in
        // Start, chat-box dragging in Update, inventory hit tests).
        private static readonly string[] GuiSpaceTypes = { "Script_WindowController", "Script_Window", "Inventory", "Menucontroller" };
        // Menucontroller also does world work outside OnGUI; only these of its methods are GUI space:
        // Start and ResetGUI place the HUD (hotbar, health, chat, menu buttons), Update drags the chat box.
        // A scale change looks like a resize to Script_WindowController.Update, which then calls ResetGUI.
        private static readonly string[] MenucontrollerGuiSpaceMethods = { "Update", "Start", "ResetGUI" };
        private static readonly string[] OnGuiTypes = { "Menucontroller", "Script_WindowController", "Scr_StartupScreen", "Scr_SceneChanger", "ConGUI" };
        // Third-party code that never runs inside the game's OnGUI.
        private static readonly string[] SkipNamespaces =
            { "UnityStandardAssets", "UnityEngine", "Gaia", "DuloGames", "ProBuilder2", "MysticArsenal", "Assets.Scripts.Pathfinding" };

        private static readonly Dictionary<MethodBase, MethodInfo> Replacements = new Dictionary<MethodBase, MethodInfo>();
        private static readonly Dictionary<MethodBase, MethodInfo> GuiSpaceReplacements = new Dictionary<MethodBase, MethodInfo>();

        internal static void Recompute()
        {
            // Off, or the game's own (stretching) /scalegui mode, which sets its GUI matrix itself.
            if (!Plugin.UiEnabled.Value || (Scr_Options.instance != null && Scr_Options.instance.scaleGUI))
            {
                Factor = 1f;
                return;
            }
            Factor = FactorFor(Plugin.UiUserScale.Value);
        }

        /// <summary>The factor a given user scale gives on this screen.</summary>
        internal static float FactorFor(float userScale)
        {
            var auto = Plugin.UiAutoScale.Value ? Screen.height / DesignHeight : 1f;
            return Mathf.Clamp(auto * userScale, 0.5f, 4f);
        }

        // Scaled only while the game's GUI is being laid out or drawn.
        public static int ScreenWidth() => Active ? Mathf.RoundToInt(Screen.width / Factor) : Screen.width;
        public static int ScreenHeight() => Active ? Mathf.RoundToInt(Screen.height / Factor) : Screen.height;
        public static Vector3 MousePosition() => Active ? Input.mousePosition / Factor : Input.mousePosition;
        public static Vector3 WorldToScreenPoint(Camera cam, Vector3 pos) => ToGui(cam.WorldToScreenPoint(pos), Active);
        public static Ray ScreenPointToRay(Camera cam, Vector3 pos) => cam.ScreenPointToRay(ToScreen(pos, Active));
        public static Vector3 ScreenToWorldPoint(Camera cam, Vector3 pos) => cam.ScreenToWorldPoint(ToScreen(pos, Active));

        // Always scaled: code that only works in GUI coordinates.
        public static int GuiScreenWidth() => Mathf.RoundToInt(Screen.width / Factor);
        public static int GuiScreenHeight() => Mathf.RoundToInt(Screen.height / Factor);
        public static Vector3 GuiMousePosition() => Input.mousePosition / Factor;
        public static Vector3 GuiWorldToScreenPoint(Camera cam, Vector3 pos) => ToGui(cam.WorldToScreenPoint(pos), true);
        public static Ray GuiScreenPointToRay(Camera cam, Vector3 pos) => cam.ScreenPointToRay(ToScreen(pos, true));
        public static Vector3 GuiScreenToWorldPoint(Camera cam, Vector3 pos) => cam.ScreenToWorldPoint(ToScreen(pos, true));

        private static Vector3 ToGui(Vector3 p, bool scaled) => scaled ? new Vector3(p.x / Factor, p.y / Factor, p.z) : p;
        private static Vector3 ToScreen(Vector3 p, bool scaled) => scaled ? new Vector3(p.x * Factor, p.y * Factor, p.z) : p;

        internal static void Patch(Harmony harmony)
        {
            Map("get_width", typeof(Screen), nameof(ScreenWidth), nameof(GuiScreenWidth));
            Map("get_height", typeof(Screen), nameof(ScreenHeight), nameof(GuiScreenHeight));
            Map("get_mousePosition", typeof(Input), nameof(MousePosition), nameof(GuiMousePosition));
            Map("WorldToScreenPoint", typeof(Camera), nameof(WorldToScreenPoint), nameof(GuiWorldToScreenPoint), typeof(Vector3));
            Map("ScreenPointToRay", typeof(Camera), nameof(ScreenPointToRay), nameof(GuiScreenPointToRay), typeof(Vector3));
            Map("ScreenToWorldPoint", typeof(Camera), nameof(ScreenToWorldPoint), nameof(GuiScreenToWorldPoint), typeof(Vector3));

            var game = typeof(Menucontroller).Assembly;
            var dynamicTranspiler = new HarmonyMethod(typeof(UiScale), nameof(TranspileDynamic));
            var guiSpaceTranspiler = new HarmonyMethod(typeof(UiScale), nameof(TranspileGuiSpace));
            int patched = 0, failed = 0;
            foreach (var type in game.GetTypes())
            {
                if (type.IsGenericTypeDefinition || SkipNamespaces.Any(ns => type.Namespace != null && type.Namespace.StartsWith(ns))) continue;
                var outer = type;
                while (outer.DeclaringType != null) outer = outer.DeclaringType;
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                             .Cast<MethodBase>().Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || !CallsAny(method)) continue;
                    var guiSpace = GuiSpaceTypes.Contains(outer.Name)
                                   && (outer.Name != "Menucontroller" || MenucontrollerGuiSpaceMethods.Contains(method.Name));
                    try
                    {
                        harmony.Patch(method, transpiler: guiSpace ? guiSpaceTranspiler : dynamicTranspiler);
                        patched++;
                    }
                    catch (Exception e)
                    {
                        failed++;
                        Plugin.Log.LogWarning($"UI scale: couldn't patch {type.Name}.{method.Name}: {e.Message}");
                    }
                }
            }

            var prefix = new HarmonyMethod(typeof(UiScale), nameof(OnGuiPrefix));
            var finalizer = new HarmonyMethod(typeof(UiScale), nameof(OnGuiFinalizer));
            foreach (var name in OnGuiTypes)
            {
                var onGui = AccessTools.Method(game.GetType(name), "OnGUI");
                if (onGui != null) harmony.Patch(onGui, prefix: prefix, finalizer: finalizer);
            }
            Plugin.Log.LogInfo($"UI scale: rewrote screen coordinates in {patched} methods ({failed} failed).");
        }

        private static void Map(string name, Type owner, string dynamicName, string guiSpaceName, params Type[] args)
        {
            var original = args.Length == 0 ? (MethodBase)AccessTools.PropertyGetter(owner, name.Substring(4)) : AccessTools.Method(owner, name, args);
            Replacements[original] = AccessTools.Method(typeof(UiScale), dynamicName);
            GuiSpaceReplacements[original] = AccessTools.Method(typeof(UiScale), guiSpaceName);
        }

        // Cheap pre-filter: does the method body call any of the mapped members?
        private static bool CallsAny(MethodBase method)
        {
            byte[] il;
            try { il = method.GetMethodBody()?.GetILAsByteArray(); }
            catch { return false; }
            if (il == null) return false;
            for (var i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F) continue; // call, callvirt
                var token = BitConverter.ToInt32(il, i + 1);
                MethodBase target;
                try { target = method.Module.ResolveMethod(token); }
                catch { continue; } // not a method token: a byte inside some other operand
                if (target != null && Replacements.ContainsKey(target)) return true;
            }
            return false;
        }

        private static IEnumerable<CodeInstruction> TranspileDynamic(IEnumerable<CodeInstruction> code) => Rewrite(code, Replacements);
        private static IEnumerable<CodeInstruction> TranspileGuiSpace(IEnumerable<CodeInstruction> code) => Rewrite(code, GuiSpaceReplacements);

        private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> code, Dictionary<MethodBase, MethodInfo> map)
        {
            foreach (var ins in code)
            {
                if ((ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt) && ins.operand is MethodBase m && map.TryGetValue(m, out var replacement))
                {
                    ins.opcode = OpCodes.Call; // instance calls become static calls taking the instance first
                    ins.operand = replacement;
                }
                yield return ins;
            }
        }

        private static void OnGuiPrefix(out bool __state)
        {
            __state = Mathf.Abs(Factor - 1f) > 0.001f;
            if (!__state) return;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Factor, Factor, 1f));
            depth++;
        }

        private static Exception OnGuiFinalizer(Exception __exception, bool __state)
        {
            if (__state) depth--;
            return __exception;
        }
    }
}
