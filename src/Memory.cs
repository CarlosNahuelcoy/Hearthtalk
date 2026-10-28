using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using NSMedieval.State;
using UnityEngine;

namespace Hearthtalk
{
    internal class MemoryEntry
    {
        public int Day;
        public string Date;
        public string Text;
    }

    /// <summary>
    /// Long-term memory: at the end of each game day every colonist who talked writes a short private diary entry
    /// from that day's dialogue and chats. The latest entries go into every prompt about them.
    /// </summary>
    internal static class Memory
    {
        private static readonly Dictionary<string, List<MemoryEntry>> cache = new Dictionary<string, List<MemoryEntry>>();
        private static readonly Queue<(HumanoidInstance h, int day)> queue = new Queue<(HumanoidInstance, int)>();
        private static int lastDay = -1;
        private static bool inFlight;
        private static float nextCheck;
        internal static int Version;

        internal static void OnVillageChanged()
        {
            cache.Clear();
            queue.Clear();
            lastDay = -1;
            inFlight = false;
            Version++;
        }

        internal static List<MemoryEntry> Get(HumanoidInstance h)
        {
            string key = Game.ColonistKey(h);
            if (cache.TryGetValue(key, out var list)) return list;
            list = new List<MemoryEntry>();
            try
            {
                string path = PathFor(key);
                if (File.Exists(path)) list = JsonConvert.DeserializeObject<List<MemoryEntry>>(File.ReadAllText(path)) ?? list;
            }
            catch (Exception e) { HearthtalkPlugin.Log.LogWarning("memory load failed: " + e.Message); }
            cache[key] = list;
            return list;
        }

        internal static void Clear(HumanoidInstance h)
        {
            string key = Game.ColonistKey(h);
            cache[key] = new List<MemoryEntry>();
            try { File.Delete(PathFor(key)); } catch { }
            Version++;
        }

        internal static void Remove(HumanoidInstance h, MemoryEntry e)
        {
            var list = Get(h);
            if (list.Remove(e)) Save(Game.ColonistKey(h), list);
        }

        internal static string PromptFor(HumanoidInstance h)
        {
            if (!Cfg.MemoryEnabled.Value) return null;
            var list = Get(h);
            if (list.Count == 0) return null;
            var sb = new StringBuilder("=== YOUR MEMORIES OF PAST DAYS (oldest first) ===\n");
            foreach (var m in list.Skip(Math.Max(0, list.Count - Cfg.MemoryDaysInPrompt.Value))) sb.AppendLine($"[{m.Date}] {m.Text}");
            return sb.ToString();
        }

        internal static void Update()
        {
            if (!Cfg.MemoryEnabled.Value || !Game.InVillage || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 3f;
            int day = Game.Day();
            if (lastDay < 0) lastDay = day;
            if (day != lastDay)
            {
                int ended = lastDay;
                lastDay = day;
                if (day > ended)
                    foreach (var h in Game.Workers()) queue.Enqueue((h, ended));
            }
            if (inFlight || queue.Count == 0 || !Conversations.LLMReady()) return;
            var (who, d) = queue.Dequeue();
            if (who == null || who.HasDisposed) return;
            Summarize(who, d);
        }

        /// <summary>Writes today's entry now instead of waiting for the night (used by the memory window).</summary>
        internal static void SummarizeNow(HumanoidInstance h, Action<string> onDone)
        {
            if (inFlight) { onDone?.Invoke(T.Get("Ya se está escribiendo otro recuerdo.", "Another memory is being written.")); return; }
            Summarize(h, Game.Day(), onDone);
        }

        private static void Summarize(HumanoidInstance h, int day, Action<string> onDone = null)
        {
            string key = Game.ColonistKey(h);
            var lines = ChatLog.Entries.Where(e => e.Day == day && e.Kind != LogKind.Native && e.Participants != null && e.Participants.Contains(key)).ToList();
            if (lines.Count == 0) { onDone?.Invoke(T.Get("Hoy no habló con nadie.", "They talked to no one today.")); return; }

            var sb = new StringBuilder();
            sb.AppendLine($"You are {h.GetFullName()}, a villager in a medieval English village. The player is the lord of the village.");
            sb.AppendLine(Game.ColonistContext(h, false));
            sb.AppendLine("Here is everything you said, heard or were told today (in order):");
            foreach (var e in lines.Skip(Math.Max(0, lines.Count - 60)))
                sb.AppendLine(e.Kind == LogKind.System ? $"(effect: {e.Text})" : $"{e.Speaker}: {e.Text}");
            sb.AppendLine();
            sb.AppendLine($"Write your private memory of this day in first person, in {T.StoryLanguage}: 2 to 4 sentences about what mattered — who you talked to, what the lord said or asked of you, what you promised, and how you feel about it.");
            sb.AppendLine("Only use facts from the lines above. Plain prose: no title, no date, no lists, no quotes.");

            inFlight = true;
            LLMClient.Complete(new List<Msg> { new Msg("user", sb.ToString()) }, 300, "MEMORY_" + Game.FirstName(h), (text, error) =>
            {
                inFlight = false;
                if (error != null) { onDone?.Invoke("Error: " + error); return; }
                text = text?.Trim();
                if (string.IsNullOrEmpty(text)) { onDone?.Invoke("—"); return; }
                var list = Get(h);
                list.RemoveAll(m => m.Day == day);
                list.Add(new MemoryEntry { Day = day, Date = DateFor(lines), Text = text });
                if (list.Count > 120) list.RemoveRange(0, list.Count - 120);
                Save(key, list);
                onDone?.Invoke(null);
            });
        }

        private static string DateFor(List<LogEntry> lines)
        {
            string t = lines.Last().Time ?? "";
            int comma = t.IndexOf(',');
            return comma > 0 ? t.Substring(0, comma) : t;
        }

        private static void Save(string key, List<MemoryEntry> list)
        {
            Version++;
            try { File.WriteAllText(PathFor(key), JsonConvert.SerializeObject(list, Formatting.Indented)); }
            catch (Exception e) { HearthtalkPlugin.Log.LogWarning("memory save failed: " + e.Message); }
        }

        private static string PathFor(string colonistKey)
        {
            string safe = string.Concat(colonistKey.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            return Path.Combine(ChatLog.VillageDir(Game.VillageKey), "memories_" + safe + ".json");
        }
    }
}
