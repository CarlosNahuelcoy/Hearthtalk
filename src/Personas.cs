using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NSMedieval.State;

namespace Hearthtalk
{
    /// <summary>Player-written personality for a colonist, saved per village. It leads every prompt about them.</summary>
    internal static class Personas
    {
        private static readonly Dictionary<string, string> cache = new Dictionary<string, string>();

        internal static void OnVillageChanged() => cache.Clear();

        internal static string Get(HumanoidInstance h)
        {
            string key = Game.ColonistKey(h);
            if (cache.TryGetValue(key, out var text)) return text;
            try
            {
                string path = PathFor(key);
                text = File.Exists(path) ? File.ReadAllText(path).Trim() : "";
            }
            catch (Exception e) { HearthtalkPlugin.Log.LogWarning("persona load failed: " + e.Message); text = ""; }
            cache[key] = text;
            return text;
        }

        internal static void Set(HumanoidInstance h, string text)
        {
            string key = Game.ColonistKey(h);
            text = (text ?? "").Trim();
            cache[key] = text;
            try
            {
                string path = PathFor(key);
                if (text.Length == 0) { if (File.Exists(path)) File.Delete(path); }
                else File.WriteAllText(path, text);
            }
            catch (Exception e) { HearthtalkPlugin.Log.LogWarning("persona save failed: " + e.Message); }
        }

        private static string PathFor(string colonistKey)
        {
            string safe = string.Concat(colonistKey.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            return Path.Combine(ChatLog.VillageDir(Game.VillageKey), "persona_" + safe + ".txt");
        }
    }
}
