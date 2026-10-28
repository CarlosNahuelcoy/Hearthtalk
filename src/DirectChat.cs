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
    internal class ChatTurn
    {
        public bool FromPlayer;
        public string Text;
        public string Time;
        public bool Unprompted;       // the colonist came to the lord on their own
        public List<string> Notes;    // what the reply changed in the game (mood, jobs, schedule)
        [JsonIgnore] public bool IsError; // shown in the window, never saved or sent back to the AI
    }

    /// <summary>One-to-one chat between the player (the village lord) and a colonist, saved per village.</summary>
    internal static class DirectChat
    {
        private const int HistoryForPrompt = 20;
        private static readonly Dictionary<string, List<ChatTurn>> histories = new Dictionary<string, List<ChatTurn>>();
        private static readonly HashSet<string> waiting = new HashSet<string>();
        internal static string LastError;
        internal static int Version; // bumps on every change so the chat window redraws

        internal static void OnVillageChanged() => histories.Clear();

        internal static bool IsWaiting(HumanoidInstance h) => waiting.Contains(Game.ColonistKey(h));

        internal static List<ChatTurn> History(HumanoidInstance h)
        {
            string key = Game.ColonistKey(h);
            if (histories.TryGetValue(key, out var list)) return list;
            list = new List<ChatTurn>();
            try
            {
                string path = PathFor(key);
                if (File.Exists(path)) list = JsonConvert.DeserializeObject<List<ChatTurn>>(File.ReadAllText(path)) ?? list;
            }
            catch (Exception e) { HearthtalkPlugin.Log.LogWarning("chat history load failed: " + e.Message); }
            histories[key] = list;
            return list;
        }

        internal static void Clear(HumanoidInstance h)
        {
            string key = Game.ColonistKey(h);
            histories[key] = new List<ChatTurn>();
            try { File.Delete(PathFor(key)); } catch { }
        }

        internal static void Send(HumanoidInstance h, string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text) || IsWaiting(h)) return;
            string key = Game.ColonistKey(h);
            var hist = History(h);
            hist.RemoveAll(t => t.IsError);
            hist.Add(new ChatTurn { FromPlayer = true, Text = text, Time = Game.Now() });
            Save(key, hist);
            ChatLog.Add(LogKind.Player, T.Get("Tú", "You") + " → " + Game.FirstName(h), text, key);
            RequestReply(h);
        }

        // ---------- editing the conversation ----------

        /// <summary>Asks for the colonist's answer to everything said so far.</summary>
        private static void RequestReply(HumanoidInstance h)
        {
            string key = Game.ColonistKey(h);
            var hist = History(h);
            var msgs = new List<Msg> { new Msg("system", SystemPrompt(h)) };
            foreach (var t in hist.Where(t => !t.IsError).Skip(Math.Max(0, hist.Count(t => !t.IsError) - HistoryForPrompt)))
                msgs.Add(new Msg(t.FromPlayer ? "user" : "assistant", t.Text));

            waiting.Add(key);
            LLMClient.Complete(msgs, 400, "CHAT_" + Game.FirstName(h), (reply, error) =>
            {
                waiting.Remove(key);
                if (error != null)
                {
                    LastError = error;
                    hist.Add(new ChatTurn { FromPlayer = false, Text = "Error: " + error, Time = Game.Now(), IsError = true });
                    Version++;
                    return;
                }
                LastError = null;
                reply = Actions.Apply(h, reply, out var notes);
                if (string.IsNullOrWhiteSpace(reply)) reply = "…";
                hist.Add(new ChatTurn { FromPlayer = false, Text = reply, Time = Game.Now(), Notes = notes.Count > 0 ? notes : null });
                Save(key, hist);
                ChatLog.Add(LogKind.Reply, h.GetFullName(), reply, key);
                if (Cfg.ShowBubbles.Value && !h.HasDisposed)
                    Bubbles.Say(h, FirstSentence(reply), 0f, Mathf.Max(4f, Cfg.BubbleSecondsPerLine.Value));
            });
        }

        /// <summary>The last reply can be asked for again when the colonist is answering the lord (not when they came on their own).</summary>
        internal static bool CanRegenerate(HumanoidInstance h)
        {
            var hist = History(h);
            int last = hist.FindLastIndex(t => t.FromPlayer);
            return last >= 0 && !IsWaiting(h) && hist.Skip(last + 1).All(t => !t.Unprompted);
        }

        /// <summary>Drops the colonist's answer to the lord's last message and asks again, with everything else as it was.</summary>
        internal static void Regenerate(HumanoidInstance h)
        {
            if (!CanRegenerate(h)) return;
            var hist = History(h);
            int last = hist.FindLastIndex(t => t.FromPlayer);
            Forget(h, hist.GetRange(last + 1, hist.Count - last - 1));
            hist.RemoveRange(last + 1, hist.Count - last - 1);
            Save(Game.ColonistKey(h), hist);
            RequestReply(h);
        }

        /// <summary>Changes one of the lord's messages: what came after it no longer happened, and the colonist answers again.</summary>
        internal static void EditPlayerMessage(HumanoidInstance h, int index, string text)
        {
            text = text?.Trim();
            var hist = History(h);
            if (string.IsNullOrEmpty(text) || IsWaiting(h) || index < 0 || index >= hist.Count || !hist[index].FromPlayer) return;
            Forget(h, hist.GetRange(index, hist.Count - index));
            hist.RemoveRange(index, hist.Count - index);
            hist.Add(new ChatTurn { FromPlayer = true, Text = text, Time = Game.Now() });
            Save(Game.ColonistKey(h), hist);
            ChatLog.Add(LogKind.Player, T.Get("Tú", "You") + " → " + Game.FirstName(h), text, Game.ColonistKey(h));
            RequestReply(h);
        }

        /// <summary>Rewrites what the colonist said, by hand.</summary>
        internal static void EditReply(HumanoidInstance h, int index, string text)
        {
            text = text?.Trim();
            var hist = History(h);
            if (string.IsNullOrEmpty(text) || index < 0 || index >= hist.Count || hist[index].FromPlayer || hist[index].IsError) return;
            ChatLog.Replace(Game.ColonistKey(h), hist[index].Text, text);
            hist[index].Text = text;
            Save(Game.ColonistKey(h), hist);
        }

        /// <summary>Deletes a message. Deleting one of the lord's messages also deletes the colonist's answer to it.</summary>
        internal static void Delete(HumanoidInstance h, int index)
        {
            var hist = History(h);
            if (IsWaiting(h) || index < 0 || index >= hist.Count) return;
            int end = index + 1;
            if (hist[index].FromPlayer)
                while (end < hist.Count && !hist[end].FromPlayer && !hist[end].Unprompted) end++;
            var removed = hist.GetRange(index, end - index);
            Forget(h, removed);
            hist.RemoveRange(index, end - index);
            Save(Game.ColonistKey(h), hist);
        }

        /// <summary>Removed lines also leave the village log, so they do not end up in the colonist's memories.</summary>
        private static void Forget(HumanoidInstance h, List<ChatTurn> turns)
        {
            foreach (var t in turns.Where(t => !t.IsError)) ChatLog.Remove(Game.ColonistKey(h), t.Text);
        }

        /// <summary>The colonist speaks first. The instruction describes the situation and is not kept in the history.</summary>
        internal static void Unprompted(HumanoidInstance h, string instruction, Action<string, string> onDone)
        {
            string key = Game.ColonistKey(h);
            if (IsWaiting(h)) { onDone(null, "busy"); return; }
            var hist = History(h);
            var msgs = new List<Msg> { new Msg("system", SystemPrompt(h, withActions: false)) };
            foreach (var t in hist.Skip(Math.Max(0, hist.Count - HistoryForPrompt)))
                msgs.Add(new Msg(t.FromPlayer ? "user" : "assistant", t.Text));
            msgs.Add(new Msg("user", instruction));
            waiting.Add(key);
            LLMClient.Complete(msgs, 300, "PRIVATE_" + Game.FirstName(h), (reply, error) =>
            {
                waiting.Remove(key);
                if (error != null) { onDone(null, error); return; }
                hist.RemoveAll(t => t.IsError);
                reply = Actions.Apply(null, reply, out _)?.Trim().Trim('"', '«', '»');
                if (string.IsNullOrWhiteSpace(reply) || reply.Trim('.').Equals("SKIP", StringComparison.OrdinalIgnoreCase) || h.HasDisposed) { onDone(reply, null); return; }
                hist.Add(new ChatTurn { FromPlayer = false, Text = reply, Time = Game.Now(), Unprompted = true });
                Save(key, hist);
                ChatLog.Add(LogKind.Reply, h.GetFullName() + " → " + T.Get("ti", "you"), reply, key);
                onDone(reply, null);
            });
        }

        internal static string SystemPrompt(HumanoidInstance h, bool withActions = true)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"You are {h.GetFullName()}, a villager in a medieval English settlement (Going Medieval, 14th century, after the Black Death). Stay in character at all times.");
            sb.AppendLine("The person talking to you is the lord of the village — the one who directs the settlement and its work. Speak to them as your nature, mood and position suggest: respectful, warm, wary, blunt or bitter.");
            sb.AppendLine($"Always answer in {T.StoryLanguage}.");
            sb.AppendLine();
            sb.AppendLine("HOW TO ANSWER:");
            sb.AppendLine("- Speak in first person, out loud, like a real person in a conversation. Usually 1 to 4 sentences.");
            sb.AppendLine("- No narration, no actions between asterisks, no lists, no modern words or ideas.");
            sb.AppendLine("- Your feelings come from the state, needs and recent events below: if you are hungry, tired or miserable, it shows.");
            sb.AppendLine("- Only refer to people, places, buildings and events that appear below or that the lord has mentioned. If you do not know something, say so.");
            sb.AppendLine("- You cannot see the future, the game's menus or numbers; talk about how things feel, not about statistics.");
            sb.AppendLine("- Lines marked [Situation, not said by the lord] describe what is going on; they are not words from the lord.");
            if (!string.IsNullOrWhiteSpace(Cfg.GlobalPrompt.Value))
                sb.AppendLine("\nPLAYER INSTRUCTIONS (always follow):\n" + Cfg.GlobalPrompt.Value.Trim());
            sb.AppendLine();
            sb.AppendLine(Game.SettlementContext());
            sb.AppendLine("=== WHO YOU ARE ===");
            sb.AppendLine(Game.ColonistContext(h, true));
            string work = Actions.WorkContext(h);
            if (!string.IsNullOrEmpty(work)) sb.AppendLine(work);
            string memories = Memory.PromptFor(h);
            if (!string.IsNullOrEmpty(memories)) sb.AppendLine(memories);

            var memory = ChatLog.MemoryOf(Game.ColonistKey(h), 14).Where(e => e.Kind == LogKind.Dialogue).ToList();
            if (memory.Count > 0)
            {
                sb.AppendLine("=== THINGS YOU RECENTLY SAID OR HEARD IN THE VILLAGE ===");
                foreach (var e in memory) sb.AppendLine($"[{e.Time}] {e.Speaker}: {e.Text}");
            }
            if (withActions)
            {
                string actions = Actions.PromptSection(h);
                if (!string.IsNullOrEmpty(actions)) sb.AppendLine().AppendLine(actions);
            }
            return sb.ToString();
        }

        internal static string FirstSentence(string s)
        {
            if (s.Length <= 140) return s;
            int cut = s.IndexOfAny(new[] { '.', '!', '?' }, 40);
            return cut > 0 && cut < 160 ? s.Substring(0, cut + 1) : s.Substring(0, 137) + "…";
        }

        private static void Save(string key, List<ChatTurn> hist)
        {
            Version++;
            try { File.WriteAllText(PathFor(key), JsonConvert.SerializeObject(hist.Where(t => !t.IsError).ToList(), Formatting.Indented)); }
            catch (Exception e) { HearthtalkPlugin.Log.LogWarning("chat history save failed: " + e.Message); }
        }

        private static string PathFor(string colonistKey)
        {
            string safe = string.Concat(colonistKey.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            return Path.Combine(ChatLog.VillageDir(Game.VillageKey), "chat_" + safe + ".json");
        }
    }
}
