using System;
using HarmonyLib;

namespace GSOHDTextures
{
    /// <summary>
    /// The /weather chat command. Weather runs in this plugin, client side, so the command is answered here: the
    /// message is caught before Scr_RPCSender sends it (so the server never sees it) and the reply goes to the chat.
    ///   /weather                         what it is now, and what's coming
    ///   /weather clear|cloudy|overcast|rain|fog   switch to it and keep it
    ///   /weather auto                    back to the changing schedule
    /// </summary>
    internal static class WeatherCommand
    {
        internal static void Patch(Harmony harmony)
        {
            var send = AccessTools.Method(typeof(Scr_RPCSender), nameof(Scr_RPCSender.sendChatMessage));
            harmony.Patch(send, prefix: new HarmonyMethod(typeof(WeatherCommand), nameof(BeforeSend)));
        }

        // Returning false keeps the message from being sent.
        private static bool BeforeSend(string msg)
        {
            if (msg == null) return true;
            string text = msg.Trim();
            if (!text.Equals("/weather", StringComparison.OrdinalIgnoreCase) && !text.StartsWith("/weather ", StringComparison.OrdinalIgnoreCase))
                return true;
            try
            {
                Handle(text.Substring("/weather".Length).Trim().ToLowerInvariant());
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[weather] /weather failed: {e}");
            }
            return false;
        }

        private static void Handle(string arg)
        {
            var w = Weather.Instance;
            if (w == null)
            {
                Reply("Weather is off (Weather.Enabled in gso.hdtextures.cfg).", "red");
                return;
            }
            if (arg.Length == 0)
            {
                Reply($"Weather: {Name(w.Current)}{(w.Held ? " (kept until /weather auto)" : $", changing in about {Math.Max(1, w.SecondsToChange / 60)} min")}. Time {(int)w.Hour:00}:{(int)(w.Hour % 1f * 60f):00}. Use /weather clear, cloudy, overcast, rain, fog or auto.");
                return;
            }
            if (arg == "auto")
            {
                w.Auto();
                Reply("Weather changes on its own again.");
                return;
            }
            if (!TryPick(arg, out var pattern))
            {
                Reply($"Unknown weather '{arg}'. Use clear, cloudy, overcast, rain, fog or auto.", "red");
                return;
            }
            w.Set(pattern, true);
            Reply($"Weather: {Name(pattern)}, blending in over a minute and kept until /weather auto.");
        }

        private static bool TryPick(string arg, out Weather.Pattern p)
        {
            switch (arg)
            {
                case "sunny": case "clear": p = Weather.Pattern.Clear; return true;
                case "cloudy": case "partlycloudy": case "partly": p = Weather.Pattern.PartlyCloudy; return true;
                case "overcast": case "grey": case "gray": p = Weather.Pattern.Overcast; return true;
                case "rain": case "rainy": case "storm": p = Weather.Pattern.Rain; return true;
                case "fog": case "foggy": case "mist": p = Weather.Pattern.Fog; return true;
            }
            return Weather.TryParse(arg, out p);
        }

        private static string Name(Weather.Pattern p)
        {
            switch (p)
            {
                case Weather.Pattern.PartlyCloudy: return "partly cloudy";
                default: return p.ToString().ToLowerInvariant();
            }
        }

        private static void Reply(string text, string colour = "yellow")
        {
            Plugin.Log.LogInfo("[weather] " + text);
            if (Menucontroller.instance != null) Menucontroller.instance.sendChatMessage("color=" + colour + "|" + text.Replace('|', '/'), 0);
        }
    }
}
