using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NSEipix.Base;
using NSMedieval.GameEventSystem;
using UnityEngine;

namespace Hearthtalk
{
    /// <summary>The village's chronicler: comments on what happens and, if allowed, can bring events when the lord asks for them.</summary>
    internal static class Storyteller
    {
        internal class Turn { public bool FromPlayer; public string Text; public string Time; public List<string> Notes; }

        /// <summary>Events the narrator may start: visitors, trade, weather and fights the game already knows how to run.</summary>
        internal static readonly (string id, string what)[] Events =
        {
            ("game_event_trader_visitor", "a travelling trader"),
            ("game_event_general_goods_trader_normal", "a goods caravan"),
            ("game_event_trader_weapons", "a weapons trader"),
            ("game_event_trader_rare", "a trader with rare goods"),
            ("game_event_bard_visitor", "a bard visits"),
            ("game_event_priest_visitor", "a priest visits"),
            ("game_event_shaman_visitor", "a pagan shaman visits"),
            ("game_event_beggar", "a beggar asks for help"),
            ("game_event_friendly_gift", "a friendly faction sends a gift"),
            ("game_event_new_worker", "a stranger asks to join the village"),
            ("game_event_mercenaries_for_hire_Construction", "mercenary builders for hire"),
            ("game_event_cold_snap", "a cold snap"),
            ("game_event_heat_wave", "a heat wave"),
            ("game_event_hailstorm", "a hailstorm"),
            ("game_event_thunderstorm", "a thunderstorm"),
            ("game_event_crop_blight", "crop blight"),
            ("game_event_rats_pest", "rats in the stores"),
            ("game_event_wolf_raid", "a wolf attack"),
            ("game_event_boar_raid", "a boar attack"),
            ("game_event_bear_raid", "a bear attack"),
            ("game_event_raid_new", "a raid by bandits"),
        };

        private static List<Turn> history;
        private static string loadedFor;
        internal static bool Waiting;
        internal static int Version;
        private static float nextComment;
        private static bool commentInFlight;

        internal static void OnVillageChanged() { history = null; loadedFor = null; Waiting = false; Version++; }

        internal static List<Turn> History()
        {
            if (history != null && loadedFor == Game.VillageKey) return history;
            loadedFor = Game.VillageKey;
            history = new List<Turn>();
            try
            {
                string p = PathFor();
                if (File.Exists(p)) history = JsonConvert.DeserializeObject<List<Turn>>(File.ReadAllText(p)) ?? history;
            }
            catch (Exception e) { HearthtalkPlugin.Log.LogWarning("storyteller load failed: " + e.Message); }
            return history;
        }

        internal static void Clear()
        {
            history = new List<Turn>();
            Version++;
            try { File.Delete(PathFor()); } catch { }
        }

        internal static void Send(string text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text) || Waiting) return;
            var hist = History();
            hist.Add(new Turn { FromPlayer = true, Text = text, Time = Game.Now() });
            Save();
            var msgs = new List<Msg> { new Msg("system", SystemPrompt(true)) };
            foreach (var t in hist.Skip(Math.Max(0, hist.Count - 20)))
                msgs.Add(new Msg(t.FromPlayer ? "user" : "assistant", t.Text));
            Waiting = true;
            Version++;
            LLMClient.Complete(msgs, 500, "STORYTELLER", (reply, error) =>
            {
                Waiting = false;
                Version++;
                if (error != null) { hist.Add(new Turn { Text = "Error: " + error, Time = Game.Now() }); return; }
                var notes = new List<string>();
                reply = Regex.Replace(reply, @"\[\s*EVENT\s*:\s*([a-zA-Z0-9_]+)\s*\]", m => { var n = StartEvent(m.Groups[1].Value); if (n != null) notes.Add(n); return ""; }).Trim();
                hist.Add(new Turn { Text = reply, Time = Game.Now(), Notes = notes.Count > 0 ? notes : null });
                Save();
                foreach (var n in notes) ChatLog.Add(LogKind.Narrator, Name, n);
            });
        }

        private static string StartEvent(string id)
        {
            if (!Cfg.StorytellerCanTriggerEvents.Value || !Events.Any(e => e.id == id)) return null;
            try
            {
                if (!MonoSingleton<NSMedieval.GameEventSystem.GameEventSystem>.IsInstantiated()) return null;
                bool ok = MonoSingleton<NSMedieval.GameEventSystem.GameEventSystem>.Instance.StartEvent(id);
                string what = Events.First(e => e.id == id).what;
                return ok ? T.Get("Evento iniciado: ", "Event started: ") + what : T.Get("El destino no pudo traer: ", "Fate could not bring: ") + what + T.Get(" (no es posible ahora)", " (not possible right now)");
            }
            catch (Exception e)
            {
                HearthtalkPlugin.Log.LogWarning("storyteller event failed: " + e.Message);
                return null;
            }
        }

        internal static string Name => T.Get("El cronista", "The chronicler");

        private static string SystemPrompt(bool chat)
        {
            var sb = new StringBuilder();
            string persona = Cfg.StorytellerPrompt.Value;
            sb.AppendLine(string.IsNullOrWhiteSpace(persona)
                ? "You are the chronicler of this village: an old, wry, all-seeing narrator who has watched its fate from the start. You speak about the village with dry humour, fondness and the occasional ominous hint, like a medieval chronicle come alive."
                : "You are the narrator of this village. " + persona.Trim());
            sb.AppendLine("The user is the lord of the village (the player). You are not a villager; you know everyone and everything that happened.");
            sb.AppendLine($"Always write in {T.StoryLanguage}. Keep replies short (1-5 sentences). No lists, no game numbers or menus.");
            if (!string.IsNullOrWhiteSpace(Cfg.GlobalPrompt.Value)) sb.AppendLine("PLAYER INSTRUCTIONS (always follow):\n" + Cfg.GlobalPrompt.Value.Trim());
            sb.AppendLine();
            sb.AppendLine(Game.SettlementContext());
            sb.AppendLine("Villagers: " + string.Join("; ", Game.Workers().Select(w => $"{w.GetFullName()} ({Game.MoodWord(w)})")));
            var recent = ChatLog.Entries.Where(e => e.Kind != LogKind.Native).Reverse().Take(15).Reverse().ToList();
            if (recent.Count > 0)
            {
                sb.AppendLine("Latest things said in the village:");
                foreach (var e in recent) sb.AppendLine($"[{e.Time}] {e.Speaker}: {e.Text}");
            }
            if (chat && Cfg.StorytellerCanTriggerEvents.Value)
            {
                sb.AppendLine();
                sb.AppendLine("YOU CAN BEND FATE: when the lord asks for something to happen (or you decide the story needs it and the lord agreed), end your reply with one tag [EVENT:id]. At most one per reply, never mention the tag. Available:");
                foreach (var e in Events) sb.AppendLine($"- {e.id}: {e.what}");
                sb.AppendLine("Do not bring disasters or attacks unless the lord clearly asks for them.");
            }
            return sb.ToString();
        }

        /// <summary>A one-line comment in the live log when something starts.</summary>
        internal static void OnGameEvent(string eventId)
        {
            if (!Cfg.StorytellerEnabled.Value || !Cfg.StorytellerComments.Value || commentInFlight || Time.unscaledTime < nextComment || !Conversations.LLMReady() || !Game.InVillage) return;
            if (eventId.StartsWith("game_event_season_start_") || eventId.Contains("_influence_") || eventId.StartsWith("infamy")) return;
            commentInFlight = true;
            nextComment = Time.unscaledTime + 60f;
            var msgs = new List<Msg>
            {
                new Msg("system", SystemPrompt(false)),
                new Msg("user", $"[The game just started this event: {eventId.Replace("game_event_", "").Replace('_', ' ')}] Write one short sentence for the village chronicle about it, in your voice. Only the sentence."),
            };
            LLMClient.Complete(msgs, 120, "STORY_COMMENT", (text, error) =>
            {
                commentInFlight = false;
                if (error == null && !string.IsNullOrWhiteSpace(text)) ChatLog.Add(LogKind.Narrator, Name, text.Trim().Trim('"'));
            });
        }

        private static void Save()
        {
            Version++;
            try { File.WriteAllText(PathFor(), JsonConvert.SerializeObject(history, Formatting.Indented)); }
            catch (Exception e) { HearthtalkPlugin.Log.LogWarning("storyteller save failed: " + e.Message); }
        }

        private static string PathFor() => Path.Combine(ChatLog.VillageDir(Game.VillageKey), "storyteller.json");
    }
}
