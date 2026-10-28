using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NSMedieval.State;
using UnityEngine;

namespace Hearthtalk
{
    /// <summary>A meeting: the lord talks to several villagers at once and they answer, argue and agree among themselves.</summary>
    internal static class GroupChat
    {
        internal class Turn { public string Speaker; public string Text; public bool FromPlayer; public List<string> Notes; }

        internal static readonly List<HumanoidInstance> Members = new List<HumanoidInstance>();
        internal static readonly List<Turn> Transcript = new List<Turn>();
        internal static bool Waiting;
        internal static int Version;
        internal const int MaxMembers = 6;

        internal static void OnVillageChanged() { Members.Clear(); Transcript.Clear(); Waiting = false; Version++; }

        /// <summary>Everyone selected in the game, or else the colonist plus whoever stands near them.</summary>
        internal static void Start(HumanoidInstance around)
        {
            var group = Game.SelectedWorkers();
            if (group.Count < 2 && around != null)
            {
                var pos = around.GetPosition();
                group = new List<HumanoidInstance> { around };
                group.AddRange(Game.Workers().Where(w => w != around && Game.Safe(() => Vector3.Distance(w.GetPosition(), pos)) < 14f)
                    .OrderBy(w => Vector3.Distance(w.GetPosition(), pos)));
            }
            if (group.Count < 2) group = Game.Workers();
            Members.Clear();
            Members.AddRange(group.Take(MaxMembers));
            Transcript.Clear();
            Waiting = false;
            Version++;
        }

        internal static void Send(string text)
        {
            text = text?.Trim();
            Members.RemoveAll(m => m == null || m.HasDisposed);
            if (string.IsNullOrEmpty(text) || Waiting || Members.Count == 0) return;
            Transcript.Add(new Turn { Speaker = T.Get("Tú", "You"), Text = text, FromPlayer = true });
            Version++;
            string[] keys = Members.Select(Game.ColonistKey).ToArray();
            ChatLog.Add(LogKind.Player, T.Get("Tú", "You") + " → " + string.Join(", ", Members.Select(Game.FirstName)), text, keys);

            var msgs = new List<Msg> { new Msg("system", SystemPrompt()) };
            foreach (var t in Transcript.Skip(Math.Max(0, Transcript.Count - 30)))
                msgs.Add(t.FromPlayer ? new Msg("user", t.Text) : new Msg("assistant", $"{t.Speaker}: {t.Text}"));
            msgs.Add(new Msg("user", "[Answer now with the JSON array.]"));

            Waiting = true;
            LLMClient.Complete(msgs, 700, "GROUP", (reply, error) =>
            {
                Waiting = false;
                Version++;
                if (error != null) { Transcript.Add(new Turn { Speaker = "Error", Text = error }); return; }
                foreach (var (who, raw) in Parse(reply))
                {
                    string line = Actions.Apply(who, raw, out var notes);
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    Transcript.Add(new Turn { Speaker = who.GetFullName(), Text = line, Notes = notes.Count > 0 ? notes : null });
                    ChatLog.Add(LogKind.Reply, who.GetFullName(), line, keys);
                }
            });
        }

        private static string SystemPrompt()
        {
            var sb = new StringBuilder();
            sb.AppendLine("You voice several villagers of a medieval English settlement (Going Medieval, 14th century) in a meeting with the lord of the village, who is the user.");
            sb.AppendLine($"Write in {T.StoryLanguage}.");
            sb.AppendLine("OUTPUT: only a JSON array, no markdown: [{\"speaker\":\"First name\",\"text\":\"...\"}, ...]");
            sb.AppendLine("1 to 4 entries. Not everyone has to speak; those who care most answer first, and they may react to each other, disagree or joke. Each entry is 1-3 spoken sentences, in character, no narration.");
            sb.AppendLine("Only refer to people, places and events in the context or mentioned by the lord.");
            if (!string.IsNullOrWhiteSpace(Cfg.GlobalPrompt.Value)) sb.AppendLine("\nPLAYER INSTRUCTIONS (always follow):\n" + Cfg.GlobalPrompt.Value.Trim());
            sb.AppendLine();
            sb.AppendLine(Game.SettlementContext());
            foreach (var m in Members)
            {
                sb.AppendLine($"=== {m.GetFullName()} ===");
                sb.AppendLine(Game.ColonistContext(m, false));
                string work = Actions.WorkContext(m);
                if (!string.IsNullOrEmpty(work)) sb.AppendLine(work);
                string mem = Memory.PromptFor(m);
                if (!string.IsNullOrEmpty(mem)) sb.AppendLine(mem);
            }
            string actions = Actions.PromptSection(null);
            if (!string.IsNullOrEmpty(actions))
                sb.AppendLine().AppendLine(actions).AppendLine("Put a speaker's tags at the end of that speaker's own text.");
            return sb.ToString();
        }

        private static List<(HumanoidInstance, string)> Parse(string text)
        {
            var result = new List<(HumanoidInstance, string)>();
            HumanoidInstance Who(string name) => Members.FirstOrDefault(m => !string.IsNullOrEmpty(name) && Conversations.Matches(name, m));
            int s = text.IndexOf('['), e = text.LastIndexOf(']');
            if (s >= 0 && e > s)
            {
                try
                {
                    foreach (var item in JArray.Parse(text.Substring(s, e - s + 1)))
                    {
                        var who = Who((string)item["speaker"]);
                        string line = ((string)item["text"])?.Trim();
                        if (who != null && !string.IsNullOrEmpty(line)) result.Add((who, line));
                    }
                    if (result.Count > 0) return result;
                }
                catch { }
            }
            foreach (var raw in text.Split('\n'))
            {
                var m = Regex.Match(raw.Trim(), @"^\**([^:*]{2,40})\**\s*:\s*(.+)$");
                var who = m.Success ? Who(m.Groups[1].Value) : null;
                if (who != null) result.Add((who, m.Groups[2].Value.Trim().Trim('"')));
            }
            return result;
        }
    }
}
