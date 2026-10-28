using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Hearthtalk
{
    internal enum LogKind { Dialogue, Native, Player, Reply, System, Narrator }

    internal class LogEntry
    {
        public string Time;
        public string Speaker;
        public string Text;
        public LogKind Kind;
        public string[] Participants; // colonist keys involved, used as memory
        public int Day = -1;          // days since the village was founded, for daily memories
    }

    /// <summary>Everything said in the village, kept per save so no dialogue is ever lost.</summary>
    internal static class ChatLog
    {
        private const int MaxEntries = 1500;
        internal static readonly List<LogEntry> Entries = new List<LogEntry>();
        internal static int Version; // bumps on every change so the UI knows when to scroll
        private static string loadedVillage;

        internal static void Add(LogKind kind, string speaker, string text, params string[] participants)
        {
            var e = new LogEntry { Time = Game.Now(), Speaker = speaker, Text = text, Kind = kind, Participants = participants, Day = Game.Day() };
            Entries.Add(e);
            if (Entries.Count > MaxEntries) Entries.RemoveRange(0, Entries.Count - MaxEntries);
            Version++;
            if (kind == LogKind.Native) return; // the game's own lines are already in its saves
            try { File.AppendAllText(PathFor(Game.VillageKey), JsonConvert.SerializeObject(e) + "\n"); }
            catch (Exception ex) { HearthtalkPlugin.Log.LogWarning("chat log write failed: " + ex.Message); }
        }

        /// <summary>Removes the latest chat line with this text (an edited or deleted chat message).</summary>
        internal static void Remove(string colonistKey, string text) => Change(colonistKey, text, null);

        internal static void Replace(string colonistKey, string oldText, string newText) => Change(colonistKey, oldText, newText);

        private static void Change(string colonistKey, string text, string newText)
        {
            int i = Entries.FindLastIndex(e => (e.Kind == LogKind.Player || e.Kind == LogKind.Reply) && e.Text == text && e.Participants != null && e.Participants.Contains(colonistKey));
            if (i < 0) return;
            if (newText == null) Entries.RemoveAt(i); else Entries[i].Text = newText;
            Version++;
            Rewrite();
        }

        private static void Rewrite()
        {
            try { File.WriteAllLines(PathFor(Game.VillageKey), Entries.Where(e => e.Kind != LogKind.Native).Select(e => JsonConvert.SerializeObject(e))); }
            catch (Exception ex) { HearthtalkPlugin.Log.LogWarning("chat log rewrite failed: " + ex.Message); }
        }

        /// <summary>Recent AI lines a colonist said or heard, oldest first: their conversational memory.</summary>
        internal static List<LogEntry> MemoryOf(string colonistKey, int count)
        {
            return Entries.Where(e => e.Kind != LogKind.Native && e.Kind != LogKind.System && e.Participants != null && e.Participants.Contains(colonistKey))
                .Reverse().Take(count).Reverse().ToList();
        }

        internal static void OnVillageChanged()
        {
            string key = Game.VillageKey;
            if (key == loadedVillage) return;
            loadedVillage = key;
            Entries.Clear();
            Version++;
            if (string.IsNullOrEmpty(key)) return;
            try
            {
                string path = PathFor(key);
                if (!File.Exists(path)) return;
                foreach (var line in File.ReadAllLines(path).Reverse().Take(MaxEntries).Reverse())
                {
                    var e = Game.Safe(() => JsonConvert.DeserializeObject<LogEntry>(line));
                    if (e != null) Entries.Add(e);
                }
            }
            catch (Exception ex) { HearthtalkPlugin.Log.LogWarning("chat log load failed: " + ex.Message); }
        }

        internal static string VillageDir(string key)
        {
            string safe = string.Concat((string.IsNullOrEmpty(key) ? "_none" : key).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            string dir = Path.Combine(HearthtalkPlugin.DataDir, "villages", safe);
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string PathFor(string key) => Path.Combine(VillageDir(key), "chatlog.jsonl");
    }
}
