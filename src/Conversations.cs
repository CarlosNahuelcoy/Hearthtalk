using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using NSMedieval;
using NSMedieval.State;
using UnityEngine;

namespace Hearthtalk
{
    /// <summary>Turns the game's colonist conversations (topic + tone + reaction) into real dialogue.</summary>
    internal static class Conversations
    {
        private class Request { public HumanoidInstance A, B; public string NativeLine; }

        private static Request queued;   // newest conversation waiting for the cooldown
        private static bool inFlight;
        private static float nextAllowed;
        internal static string LastError;
        internal static int Seen, Generated, Failed, KeptNative;

        internal static void OnNativeConversation(HumanoidInstance a, HumanoidInstance b)
        {
            if (!Game.Safe(() => a.IsWorker() && b.IsWorker())) return;
            Seen++;
            string native = Game.Safe(() => Game.StripTags(a.LifeEventLogs.First.Value.GetLocalizedLogWithoutSprites()));
            // Drop the "Spring 1, 1353" prefix the life log adds.
            if (native != null) native = Regex.Replace(native, @"^[^\d]*\d+,\s*\d{3,4}\s*", "");

            bool aiOn = Cfg.ConversationsEnabled.Value && LLMReady();
            bool take = aiOn && !inFlight && queued == null && Time.unscaledTime >= nextAllowed;
            HearthtalkPlugin.Log.LogInfo($"conversation {a.GetFullName()} -> {b.GetFullName()}: {(take ? "AI" : aiOn ? "kept native (busy/cooldown)" : "kept native (AI off or no provider)")}");
            if (take)
            {
                queued = new Request { A = a, B = b, NativeLine = native };
                return;
            }
            KeptNative++;
            if (Cfg.LogNativeConversations.Value && native != null)
                ChatLog.Add(LogKind.Native, "", native);
        }

        internal static bool LLMReady()
        {
            switch (Cfg.Provider.Value)
            {
                case LLMProvider.Player2: return Player2Auth.IsConnected;
                case LLMProvider.Gemini: return !string.IsNullOrWhiteSpace(Cfg.GeminiKey.Value);
                case LLMProvider.OpenRouter: return !string.IsNullOrWhiteSpace(Cfg.OpenRouterKey.Value);
                default: return !string.IsNullOrWhiteSpace(Cfg.CustomEndpoint.Value);
            }
        }

        internal static void Update()
        {
            if (queued == null || inFlight || Time.unscaledTime < nextAllowed) return;
            var r = queued;
            queued = null;
            if (r.A.HasDisposed || r.B.HasDisposed) return;
            Generate(r);
        }

        private static void Generate(Request r)
        {
            inFlight = true;
            nextAllowed = Time.unscaledTime + Mathf.Max(3f, Cfg.ConversationCooldown.Value);
            string nameA = r.A.GetFullName(), nameB = r.B.GetFullName();
            string keyA = Game.ColonistKey(r.A), keyB = Game.ColonistKey(r.B);
            int lines = Mathf.Clamp(Cfg.ConversationLines.Value, 2, 8);

            var msgs = new List<Msg> { new Msg("system", SystemPrompt(lines, Game.FirstName(r.A), Game.FirstName(r.B))), new Msg("user", UserPrompt(r, lines)) };
            LLMClient.Complete(msgs, 120 + lines * 80, "CONVERSATION", (text, error) =>
            {
                inFlight = false;
                var parsed = error == null ? Parse(text, r) : null;
                if (parsed == null || parsed.Count == 0)
                {
                    LastError = error ?? "could not read the reply";
                    Failed++;
                    HearthtalkPlugin.Log.LogWarning("conversation failed: " + LastError);
                    if (r.NativeLine != null) ChatLog.Add(LogKind.Native, "", r.NativeLine);
                    return;
                }
                LastError = null;
                Generated++;
                float delay = 0f, each = Mathf.Max(2f, Cfg.BubbleSecondsPerLine.Value);
                foreach (var (who, line) in parsed)
                {
                    ChatLog.Add(LogKind.Dialogue, who.GetFullName(), line, keyA, keyB);
                    if (Cfg.ShowBubbles.Value) Bubbles.Say(who, line, delay, each);
                    delay += each * 0.85f;
                }
            });
        }

        private static string SystemPrompt(int lines, string a, string b)
        {
            return
                "You write natural, spontaneous dialogue between two villagers of a medieval English settlement (Going Medieval, 14th century, after the Black Death).\n" +
                $"Write in {T.StoryLanguage}.\n\n" +
                "OUTPUT: only a JSON array, no markdown, nothing before or after:\n" +
                $"[{{\"speaker\":\"{a}\",\"text\":\"...\"}},{{\"speaker\":\"{b}\",\"text\":\"...\"}}]\n" +
                $"Exactly {lines} entries, alternating speakers, starting with {a}.\n\n" +
                "RULES:\n" +
                "1. Each line is one or two short sentences, spoken aloud. No narration, no stage directions, no asterisks.\n" +
                "2. Follow the TOPIC and the TONE given: how the first speaker approaches it and how the other reacts must show in the lines.\n" +
                "3. Let personality, traits, mood, needs, faith and how they feel about each other colour every line.\n" +
                "4. Only mention people, places, buildings, animals and events that appear in the context. Never invent village history.\n" +
                "5. Period-appropriate everyday speech; no modern words or ideas. Never talk about the conversation itself.";
        }

        private static string UserPrompt(Request r, int lines)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(Cfg.GlobalPrompt.Value))
                sb.AppendLine("PLAYER INSTRUCTIONS (always follow):\n" + Cfg.GlobalPrompt.Value.Trim() + "\n");
            sb.AppendLine("WHAT THE GAME SAYS HAPPENED (topic, tone and reaction):");
            sb.AppendLine(r.NativeLine ?? $"{r.A.GetFullName()} chatted with {r.B.GetFullName()}.");
            sb.AppendLine();
            sb.AppendLine(Game.SettlementContext());
            sb.AppendLine("=== FIRST SPEAKER ===");
            sb.AppendLine(Game.ColonistContext(r.A, false));
            sb.AppendLine("=== SECOND SPEAKER ===");
            sb.AppendLine(Game.ColonistContext(r.B, false));

            var keyA = Game.ColonistKey(r.A); var keyB = Game.ColonistKey(r.B);
            var shared = ChatLog.Entries.Where(e => e.Kind == LogKind.Dialogue && e.Participants != null && e.Participants.Contains(keyA) && e.Participants.Contains(keyB))
                .Reverse().Take(8).Reverse().ToList();
            if (shared.Count > 0)
            {
                sb.AppendLine("=== THEIR LAST WORDS TOGETHER (do not repeat them) ===");
                foreach (var e in shared) sb.AppendLine($"{e.Speaker}: {e.Text}");
            }
            sb.AppendLine();
            sb.AppendLine($"Write the {lines}-line exchange now.");
            return sb.ToString();
        }

        private static List<(HumanoidInstance, string)> Parse(string text, Request r)
        {
            var result = new List<(HumanoidInstance, string)>();
            HumanoidInstance Who(string name, int i)
            {
                if (!string.IsNullOrEmpty(name))
                {
                    if (Matches(name, r.A)) return r.A;
                    if (Matches(name, r.B)) return r.B;
                }
                return i % 2 == 0 ? r.A : r.B;
            }

            int s = text.IndexOf('['), e = text.LastIndexOf(']');
            if (s >= 0 && e > s)
            {
                try
                {
                    var arr = JArray.Parse(text.Substring(s, e - s + 1));
                    int i = 0;
                    foreach (var item in arr)
                    {
                        string line = ((string)item["text"])?.Trim();
                        if (!string.IsNullOrEmpty(line)) result.Add((Who((string)item["speaker"], i), line));
                        i++;
                    }
                    if (result.Count > 0) return result;
                }
                catch { }
            }

            // Fallback: "Name: line" per row.
            int n = 0;
            foreach (var raw in text.Split('\n'))
            {
                var m = Regex.Match(raw.Trim(), @"^\**([^:*]{2,40})\**\s*:\s*(.+)$");
                if (!m.Success) continue;
                result.Add((Who(m.Groups[1].Value, n), m.Groups[2].Value.Trim().Trim('"')));
                n++;
            }
            return result;
        }

        internal static bool Matches(string name, HumanoidInstance h)
        {
            name = name.Trim().ToLowerInvariant();
            string full = h.GetFullName().ToLowerInvariant(), first = Game.FirstName(h).ToLowerInvariant();
            return name == full || name == first || full.StartsWith(name) || name.StartsWith(first);
        }
    }

    [HarmonyPatch(typeof(ConversationProximityInteraction), nameof(ConversationProximityInteraction.Execute))]
    internal static class Patch_Conversation
    {
        private static void Postfix(CreatureBase agent, CreatureBase target)
        {
            try
            {
                if (agent is HumanoidInstance a && target is HumanoidInstance b)
                    Conversations.OnNativeConversation(a, b);
            }
            catch (Exception e) { HearthtalkPlugin.Log.LogError(e); }
        }
    }
}
