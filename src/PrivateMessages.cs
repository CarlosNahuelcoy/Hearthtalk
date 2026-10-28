using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NSEipix.Base;
using NSMedieval;
using NSMedieval.Enums;
using NSMedieval.GameEventSystem;
using NSMedieval.Model;
using NSMedieval.State;
using NSMedieval.StatsSystem;
using NSMedieval.UI;
using UnityEngine;

namespace Hearthtalk
{
    /// <summary>
    /// Colonists who come to the lord on their own: after a raid, an injury, a visitor, when hungry or miserable, or just because.
    /// The message lands in their chat and a notice appears in the game's own list on the right; clicking it opens the chat.
    /// </summary>
    internal static class PrivateMessages
    {
        private class Trigger
        {
            public HumanoidInstance Who;   // null = pick someone
            public string Reason;          // what happened, for the prompt
            public int Weight;             // higher wins when several are waiting
        }

        private static readonly List<Trigger> pending = new List<Trigger>();
        private static readonly Dictionary<int, double> lastFrom = new Dictionary<int, double>();
        private static readonly Dictionary<int, string> lastLifeEvent = new Dictionary<int, string>();
        private static readonly Dictionary<int, WarningMessageData> notices = new Dictionary<int, WarningMessageData>();
        private static float nextCheck, lastSentReal = -9999f;
        private static double lastHour = -1;
        private static bool inFlight;

        internal static int Sent, Skipped;
        internal static Action<HumanoidInstance> OpenChat;

        internal static void OnVillageChanged()
        {
            pending.Clear();
            lastFrom.Clear();
            lastLifeEvent.Clear();
            notices.Clear();
            lastHour = -1;
            inFlight = false;
        }

        // ---------- triggers ----------

        internal static void OnGameEvent(string eventId)
        {
            if (!Cfg.PrivateMessages.Value || !Cfg.PrivateOnEvents.Value || !Game.InVillage || string.IsNullOrEmpty(eventId)) return;
            string reason = DescribeEvent(eventId);
            if (reason == null) return;
            Add(new Trigger { Reason = reason, Weight = 3 });
        }

        private static string DescribeEvent(string id)
        {
            id = id.ToLowerInvariant();
            if (id.StartsWith("game_event_season_start_")) return "A new season has just begun: " + id.Substring("game_event_season_start_".Length) + ".";
            if (id.Contains("raid") && !id.Contains("rat")) return id.Contains("bear") || id.Contains("wolf") || id.Contains("boar") || id.Contains("dog") || id.Contains("fox") || id.Contains("polecat") || id.Contains("hare") || id.Contains("deer")
                ? "Wild animals are attacking the village right now."
                : "Raiders have been spotted coming for the village. An attack is about to begin.";
            if (id.Contains("ambush")) return "There is talk of an ambush.";
            if (id.Contains("trader") || id.Contains("merchant")) return "A trader has arrived at the village.";
            if (id.Contains("new_worker")) return "A stranger has come asking to join the village.";
            if (id.Contains("refuge")) return "People from another faction are seeking refuge in the village.";
            if (id.Contains("beggar")) return "A beggar has come to the village asking for help.";
            if (id.Contains("bard")) return "A travelling bard has come to the village.";
            if (id.Contains("priest")) return "A priest has come to visit the village.";
            if (id.Contains("shaman")) return "A pagan shaman has come to visit the village.";
            if (id.Contains("envoy")) return "An envoy from another faction has arrived.";
            if (id.Contains("gift")) return "Another faction has sent a gift to the village.";
            if (id.Contains("crop_blight")) return "A blight has struck the crops.";
            if (id.Contains("rats_pest")) return "Rats have got into the stores.";
            if (id.Contains("cold_snap")) return "A bitter cold snap has hit.";
            if (id.Contains("heat_wave")) return "A heat wave has started.";
            if (id.Contains("hailstorm")) return "A hailstorm is battering the village.";
            if (id.Contains("thunderstorm")) return "A thunderstorm is raging.";
            if (id.Contains("room_flood")) return "Water is flooding part of the village.";
            if (id.Contains("mercenaries")) return "Mercenaries have arrived offering their swords for hire.";
            if (id.Contains("runaway")) return "A runaway has been seen near the village.";
            if (id.Contains("shakedown")) return "Thugs have come demanding payment from the village.";
            if (id.Contains("pilgrims")) return "Pilgrims have come to the village.";
            return null;
        }

        private static void Add(Trigger t)
        {
            pending.Add(t);
            if (pending.Count > 8) pending.RemoveAt(0);
        }

        /// <summary>Every couple of seconds: watch the colonists' life logs and needs, roll the random chance, and send at most one message.</summary>
        internal static void Update()
        {
            if (!Game.InVillage || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 2f;
            var workers = Game.Workers();

            foreach (var h in workers) WatchLifeLog(h);
            if (!Cfg.PrivateMessages.Value) { pending.Clear(); return; }

            double hour = Math.Floor(Game.TotalHours());
            if (hour != lastHour)
            {
                bool first = lastHour < 0;
                lastHour = hour;
                if (!first) HourlyChecks(workers);
            }

            if (inFlight || pending.Count == 0 || Game.Paused || !Conversations.LLMReady()) return;
            if (Time.unscaledTime - lastSentReal < Cfg.PrivateMinMinutes.Value * 60f) return;
            var trigger = pending.OrderByDescending(p => p.Weight).First();
            pending.Remove(trigger);
            var who = trigger.Who != null && Eligible(trigger.Who) ? trigger.Who : Pick(workers);
            if (who == null) return;
            Generate(who, trigger.Reason);
        }

        private static void WatchLifeLog(HumanoidInstance h)
        {
            if (!Cfg.PrivateOnLifeEvents.Value && !Cfg.Monologues.Value) return;
            var logs = Game.Safe(() => h.LifeEventLogs.Where(l => l.Type != LifeEventType.Conversation).Take(1).ToList());
            if (logs == null || logs.Count == 0) return;
            var node = logs[0];
            // The entries are structs; their text carries the date, so it tells a new entry from the old one.
            string text = Game.StripTags(Game.Safe(() => node.GetLocalizedLogWithoutSprites()));
            if (string.IsNullOrEmpty(text)) return;
            if (!lastLifeEvent.TryGetValue(h.UniqueId, out var seen)) { lastLifeEvent[h.UniqueId] = text; return; }
            if (seen == text) return;
            lastLifeEvent[h.UniqueId] = text;
            Monologues.OnLifeEvent(h, text);
            if (!Cfg.PrivateMessages.Value || !Cfg.PrivateOnLifeEvents.Value) return;
            if (node.Type != LifeEventType.Combat && node.Type != LifeEventType.Health) return;
            Add(new Trigger { Who = h, Reason = "This just happened to you: " + text, Weight = 4 });
        }

        private static void HourlyChecks(List<HumanoidInstance> workers)
        {
            if (Cfg.PrivateOnNeeds.Value)
            {
                foreach (var h in workers)
                {
                    float mood = Stat(h, StatType.Mood), hunger = Stat(h, StatType.Hunger), sleep = Stat(h, StatType.Sleep), health = Stat(h, StatType.Health);
                    string reason = null;
                    if (mood >= 0 && mood < 20) reason = "You are miserable and close to breaking down. You want the lord to know how you feel and maybe ask for something.";
                    else if (hunger >= 0 && hunger < 15) reason = "You are starving. You go to the lord about the food.";
                    else if (health >= 0 && health < 35) reason = "You are badly hurt and in pain.";
                    else if (sleep >= 0 && sleep < 10) reason = "You are exhausted and can barely stand.";
                    if (reason != null && UnityEngine.Random.value < 0.25f) Add(new Trigger { Who = h, Reason = reason, Weight = 2 });
                }
            }
            if (UnityEngine.Random.value < Cfg.PrivateRandomChance.Value)
            {
                var reasons = new[]
                {
                    "Nothing special happened; something has been on your mind lately and you want to share it with the lord.",
                    "You have an idea or a request for the lord about the village.",
                    "You want to tell the lord something about another villager.",
                    "You want to thank the lord, or complain about something, depending on how you feel.",
                    "You noticed something around the village worth telling the lord.",
                };
                Add(new Trigger { Reason = reasons[UnityEngine.Random.Range(0, reasons.Length)], Weight = 1 });
            }
        }

        private static float Stat(HumanoidInstance h, StatType t)
        {
            try { return h.Stats.GetStat(t)?.Current ?? -1f; } catch { return -1f; }
        }

        private static bool Eligible(HumanoidInstance h)
        {
            if (h == null || h.HasDisposed || Game.Safe(() => h.HasDied)) return false;
            if (Game.Safe(() => h.WorkerBehaviour.IsBanished || h.IsLeaving)) return false;
            if (DirectChat.IsWaiting(h)) return false;
            double now = Game.TotalHours();
            return !lastFrom.TryGetValue(h.UniqueId, out double last) || now < last || now - last >= Cfg.PrivatePerColonistHours.Value;
        }

        private static HumanoidInstance Pick(List<HumanoidInstance> workers)
        {
            var ok = workers.Where(Eligible).ToList();
            return ok.Count == 0 ? null : ok[UnityEngine.Random.Range(0, ok.Count)];
        }

        // ---------- writing ----------

        private static void Generate(HumanoidInstance h, string reason)
        {
            inFlight = true;
            lastFrom[h.UniqueId] = Game.TotalHours();
            lastSentReal = Time.unscaledTime;
            string instruction =
                $"[Situation, not said by the lord] {reason}\n" +
                "You decide to go to the lord and speak to them privately, on your own initiative. Write only what you say to them: 1 to 3 sentences, in character, about this situation as you feel it. " +
                "Do not greet at length; get to the point the way your character would. Use no tags.\n" +
                "If your character honestly would not bother the lord about this right now, answer exactly: SKIP";
            DirectChat.Unprompted(h, instruction, (text, error) =>
            {
                inFlight = false;
                if (error != null) { HearthtalkPlugin.Log.LogWarning("private message failed: " + error); return; }
                if (string.IsNullOrWhiteSpace(text) || text.Trim().Trim('.', '"').Equals("SKIP", StringComparison.OrdinalIgnoreCase))
                {
                    Skipped++;
                    lastSentReal = Time.unscaledTime - Cfg.PrivateMinMinutes.Value * 60f + 20f; // a SKIP should not block the next one for long
                    return;
                }
                Sent++;
                Notify(h, text);
            });
        }

        // ---------- notice in the game's list ----------

        private static void Notify(HumanoidInstance h, string text)
        {
            try
            {
                if (!MonoSingleton<WarningMessageController>.IsInstantiated()) return;
                UIIntegration.EnsureIcon();
                if (notices.TryGetValue(h.UniqueId, out var old)) Hide(h.UniqueId, old);
                string preview = text.Length > 160 ? text.Substring(0, 157) + "…" : text;
                WarningMessageData data = null;
                data = new WarningMessageData(WarningMessageCategory.News,
                    T.Get(Game.FirstName(h) + " quiere hablar contigo", Game.FirstName(h) + " wants a word"),
                    "«" + preview + "»\n\n" + T.Get("Clic para abrir el chat.", "Click to open the chat."),
                    UIIntegration.TalkIconName,
                    _ => { Hide(h.UniqueId, data); OpenChat?.Invoke(h); },
                    null,
                    _ => Hide(h.UniqueId, data));
                notices[h.UniqueId] = data;
                MonoSingleton<WarningMessageController>.Instance.ShowMessage(data);
            }
            catch (Exception e) { HearthtalkPlugin.Log.LogWarning("notice failed: " + e.Message); }
        }

        private static void Hide(int id, WarningMessageData data)
        {
            notices.Remove(id);
            try { if (data != null && MonoSingleton<WarningMessageController>.IsInstantiated()) MonoSingleton<WarningMessageController>.Instance.HideMessage(data); }
            catch { }
        }

        /// <summary>Opening the chat counts as reading the message.</summary>
        internal static void MarkRead(HumanoidInstance h)
        {
            if (h != null && notices.TryGetValue(h.UniqueId, out var data)) Hide(h.UniqueId, data);
        }

        internal static int Unread => notices.Count;
    }

    [HarmonyPatch(typeof(NSMedieval.GameEventSystem.GameEventSystem), nameof(NSMedieval.GameEventSystem.GameEventSystem.StartEvent), typeof(string), typeof(Action<GameEventInstance>))]
    internal static class Patch_GameEventStart
    {
        private static void Postfix(string eventId, bool __result)
        {
            if (!__result) return;
            try
            {
                HearthtalkPlugin.Log.LogInfo("game event started: " + eventId);
                PrivateMessages.OnGameEvent(eventId);
                Storyteller.OnGameEvent(eventId);
            }
            catch (Exception e) { HearthtalkPlugin.Log.LogWarning("event hook failed: " + e.Message); }
        }
    }
}
