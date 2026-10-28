using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace Hearthtalk
{
    /// <summary>Export a village's conversations to readable text, and carry colonists' chats over from another save.</summary>
    internal static class DataPorter
    {
        private static string VillagesRoot => Path.Combine(HearthtalkPlugin.DataDir, "villages");

        internal static string Export()
        {
            try
            {
                string dir = ChatLog.VillageDir(Game.VillageKey);
                var sb = new StringBuilder();
                sb.AppendLine($"Hearthtalk — {Game.VillageName} — {DateTime.Now:yyyy-MM-dd HH:mm}");
                foreach (var h in Game.Workers())
                {
                    sb.AppendLine().AppendLine("==================== " + h.GetFullName() + " ====================");
                    string persona = Personas.Get(h);
                    if (!string.IsNullOrEmpty(persona)) sb.AppendLine("[" + T.Get("Personalidad", "Personality") + "] " + persona);
                    foreach (var m in Memory.Get(h)) sb.AppendLine($"[{T.Get("Recuerdo", "Memory")} {m.Date}] {m.Text}");
                    foreach (var t in DirectChat.History(h))
                        sb.AppendLine($"{(t.FromPlayer ? T.Get("Tú", "You") : Game.FirstName(h))}: {t.Text}" + (t.Notes != null ? "  (" + string.Join("; ", t.Notes) + ")" : ""));
                }
                sb.AppendLine().AppendLine("==================== " + T.Get("Registro de la aldea", "Village log") + " ====================");
                foreach (var e in ChatLog.Entries.Where(e => e.Kind != LogKind.Native))
                    sb.AppendLine($"[{e.Time}] {e.Speaker}: {e.Text}");
                var story = Storyteller.History();
                if (story.Count > 0)
                {
                    sb.AppendLine().AppendLine("==================== " + Storyteller.Name + " ====================");
                    foreach (var t in story) sb.AppendLine($"{(t.FromPlayer ? T.Get("Tú", "You") : Storyteller.Name)}: {t.Text}");
                }
                string path = Path.Combine(dir, $"export_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(path, sb.ToString());
                return T.Get("Exportado: ", "Exported: ") + path;
            }
            catch (Exception e) { return "Error: " + e.Message; }
        }

        internal static void OpenFolder() => Application.OpenURL("file:///" + ChatLog.VillageDir(Game.VillageKey).Replace('\\', '/'));

        internal static List<string> OtherVillages()
        {
            try
            {
                if (!Directory.Exists(VillagesRoot)) return new List<string>();
                string current = Path.GetFileName(ChatLog.VillageDir(Game.VillageKey));
                return Directory.GetDirectories(VillagesRoot).Select(Path.GetFileName).Where(n => n != current && n != "_none").OrderBy(n => n).ToList();
            }
            catch { return new List<string>(); }
        }

        /// <summary>Copies chat, personality and memories for every colonist here whose full name matches one in the other village.</summary>
        internal static string ImportFrom(string village)
        {
            try
            {
                string src = Path.Combine(VillagesRoot, village), dst = ChatLog.VillageDir(Game.VillageKey);
                int copied = 0;
                var names = new List<string>();
                foreach (var h in Game.Workers())
                {
                    string full = h.GetFullName();
                    string mine = Safe(Game.ColonistKey(h));
                    bool any = false;
                    foreach (var prefix in new[] { "chat_", "persona_", "memories_" })
                    {
                        var match = Directory.GetFiles(src, prefix + "*").FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).EndsWith("_" + Safe(full)));
                        if (match == null) continue;
                        string target = Path.Combine(dst, prefix + mine + Path.GetExtension(match));
                        if (prefix == "chat_" && File.Exists(target))
                        {
                            // Keep what was said here too: the old chat goes first.
                            var a = JsonConvert.DeserializeObject<List<ChatTurn>>(File.ReadAllText(match)) ?? new List<ChatTurn>();
                            var b = JsonConvert.DeserializeObject<List<ChatTurn>>(File.ReadAllText(target)) ?? new List<ChatTurn>();
                            File.WriteAllText(target, JsonConvert.SerializeObject(a.Concat(b).ToList(), Formatting.Indented));
                        }
                        else File.Copy(match, target, true);
                        copied++;
                        any = true;
                    }
                    if (any) names.Add(Game.FirstName(h));
                }
                DirectChat.OnVillageChanged();
                Personas.OnVillageChanged();
                Memory.OnVillageChanged();
                return copied == 0
                    ? T.Get("No hay colonos con el mismo nombre en esa aldea.", "No colonists with the same name in that village.")
                    : T.Get("Importado: ", "Imported: ") + string.Join(", ", names);
            }
            catch (Exception e) { return "Error: " + e.Message; }
        }

        private static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
