using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NSEipix.Base;
using NSEipix.Repository;
using NSMedieval;
using NSMedieval.Controllers;
using NSMedieval.Manager;
using NSMedieval.Model;
using NSMedieval.Repository;
using NSMedieval.State;
using NSMedieval.StatsSystem;
using NSMedieval.Enums;
using UnityEngine;

namespace Hearthtalk
{
    /// <summary>Read-only access to the running game: village, colonists and the text that describes them.</summary>
    internal static class Game
    {
        private static string lastVillage;

        internal static event Action VillageChanged;

        internal static bool InVillage => GlobalSaveController.CurrentVillageData != null && Workers().Count > 0;
        internal static string VillageName => Safe(() => GlobalSaveController.CurrentVillageData.Name) ?? "";
        internal static string VillageKey => Safe(() => GlobalSaveController.CurrentVillageData.FolderName) ?? "";
        internal static bool Paused => Time.timeScale <= 0f;

        internal static void Update()
        {
            string key = GlobalSaveController.CurrentVillageData != null ? VillageKey : null;
            if (key == lastVillage) return;
            lastVillage = key;
            VillageChanged?.Invoke();
        }

        internal static List<HumanoidInstance> Workers()
        {
            return Safe(() => GlobalSaveController.CurrentVillageData?.Workers?.Where(w => w != null && !w.HasDisposed).ToList()) ?? new List<HumanoidInstance>();
        }

        internal static HumanoidInstance SelectedWorker()
        {
            if (!MonoSingleton<SelectableObjectManager>.IsInstantiated()) return null;
            if (!MonoSingleton<SelectableObjectManager>.Instance.GetFirstSelected(out var sel) || sel == null) return null;
            return Safe(() => sel.GetAsCreature()) is HumanoidInstance h && Safe(() => h.IsWorker()) ? h : null;
        }

        internal static List<HumanoidInstance> SelectedWorkers()
        {
            var list = new List<HumanoidInstance>();
            if (!MonoSingleton<SelectableObjectManager>.IsInstantiated()) return list;
            foreach (var sel in MonoSingleton<SelectableObjectManager>.Instance.SelectedObjects.ToList())
                if (Safe(() => sel.GetAsCreature()) is HumanoidInstance h && Safe(() => h.IsWorker())) list.Add(h);
            return list;
        }

        internal static string ColonistKey(HumanoidInstance h) => $"{h.UniqueId}_{h.GetFullName()}";
        internal static string FirstName(HumanoidInstance h) => Safe(() => h.Info.FirstName) ?? h.GetFullName();

        internal static string Now()
        {
            var d = Safe(() => GlobalSaveController.CurrentVillageData.DateAndTime);
            if (d == null) return "";
            return $"{SeasonName(d)} {d.Day}, {d.HoursSinceDay:00}:{d.MinutesSinceHour:00}";
        }

        /// <summary>In-game hours since the village was founded; used for cooldowns that should follow game time.</summary>
        internal static double TotalHours()
        {
            var d = Safe(() => GlobalSaveController.CurrentVillageData.DateAndTime);
            return d == null ? 0 : d.DaysTotal * 24.0 + d.HoursSinceDay + d.MinutesSinceHour / 60.0;
        }

        internal static int Day() => Safe(() => (int)GlobalSaveController.CurrentVillageData.DateAndTime.DaysTotal);

        private static string SeasonName(WorldDate d)
        {
            string s = Safe(() => d.Season.Name) ?? "";
            string loc = Loc("game_event_season_" + s.ToLowerInvariant());
            return string.IsNullOrEmpty(loc) ? s : loc;
        }

        internal static string Loc(string key)
        {
            try
            {
                string t = I2.Loc.LocalizationManager.GetTranslation(key);
                return string.IsNullOrEmpty(t) ? null : t;
            }
            catch { return null; }
        }

        internal static string StripTags(string s) => s == null ? null : Regex.Replace(s, "<[^>]+>", "").Trim();

        // ---------- context for prompts ----------

        internal static string SettlementContext()
        {
            var sb = new StringBuilder();
            var d = Safe(() => GlobalSaveController.CurrentVillageData.DateAndTime);
            sb.AppendLine($"Settlement: {VillageName}, a small village in medieval England. {Workers().Count} villagers live here.");
            if (d != null)
            {
                string part = d.HoursSinceDay < 5 ? "night" : d.HoursSinceDay < 12 ? "morning" : d.HoursSinceDay < 18 ? "afternoon" : d.HoursSinceDay < 22 ? "evening" : "night";
                sb.AppendLine($"Date: {Safe(() => d.Season.Name)} (season), day {d.Day}, year {d.Year}; it is {part} ({d.HoursSinceDay:00}:{d.MinutesSinceHour:00}). Temperature {Mathf.RoundToInt(d.TemperatureCelsius)} °C. Day {d.DaysTotal + 1} since the village was founded.");
            }
            return sb.ToString();
        }

        internal static string ColonistContext(HumanoidInstance h, bool detailed)
        {
            var sb = new StringBuilder();
            var info = h.Info;
            string persona = Personas.Get(h);
            if (!string.IsNullOrEmpty(persona))
                sb.AppendLine("PERSONALITY WRITTEN BY THE PLAYER (most important; it overrides anything below that contradicts it): " + persona);
            sb.AppendLine($"Name: {h.GetFullName()}. {info?.Age} years old, {(Safe(() => info.BodyType.ToString()) == "Female" ? "woman" : "man")}, originally from {info?.OriginTown}.");
            string bg = Clean(LocOf(info?.Background, h)), story = Clean(LocOf(info?.BackStory, h));
            if (bg != null) sb.AppendLine($"Former occupation: {bg}");
            if (story != null) sb.AppendLine($"Upbringing: {story}");

            var perks = Safe(() => h.Perks.Select(p => Clean(LocOf(p, h))).Where(x => x != null).ToList());
            if (perks?.Count > 0) sb.AppendLine("Traits: " + string.Join(" | ", perks));

            string faith = Safe(() => Repository<ReligionRepository, ReligionConfig>.Instance.GetConfigForFaith(h.Stats.GetStat(StatType.ReligiousAlignment).Current).GetID());
            if (!string.IsNullOrEmpty(faith))
                sb.AppendLine("Faith: " + (faith == "pagan" ? "follows the Old Ways (Oak Brethren, pagan)" : "follows the Church (Restitution, christian)"));

            var skills = Safe(() => h.SkillsOrdered.Where(s => s.Id != SkillType.None).ToList());
            if (skills != null)
            {
                var best = skills.OrderByDescending(s => s.Level).Take(detailed ? 6 : 3).Select(s => $"{s.Id} {s.Level}/20");
                sb.AppendLine("Best skills: " + string.Join(", ", best));
                if (detailed)
                {
                    var likes = skills.Where(s => s.GetGoalPreferenceLevel() >= 4 && s.GetGoalPreferenceLevel() <= 5).Select(s => s.Id.ToString()).ToList();
                    var hates = skills.Where(s => s.GetGoalPreferenceLevel() == 1 || s.GetGoalPreferenceLevel() == 2).Select(s => s.Id.ToString()).ToList();
                    var cannot = skills.Where(s => s.GetGoalPreferenceLevel() == 6).Select(s => s.Id.ToString()).ToList();
                    if (likes.Count > 0) sb.AppendLine("Enjoys: " + string.Join(", ", likes));
                    if (hates.Count > 0) sb.AppendLine("Dislikes doing: " + string.Join(", ", hates));
                    if (cannot.Count > 0) sb.AppendLine("Refuses or is unable to do: " + string.Join(", ", cannot));
                }
            }

            sb.AppendLine("Current state: " + StateLine(h));
            string role = Safe(() => h.WorkerBehaviour.HumanoidRoleOwner.RoleInstance?.GetID());
            if (!string.IsNullOrEmpty(role)) sb.AppendLine("Village role: " + role);

            var feelings = Safe(() => h.Stats.GetActiveEffectors().Select(e => Clean(LocOf(e.Blueprint, h))).Where(x => x != null).Distinct().Take(detailed ? 14 : 6).ToList());
            if (feelings?.Count > 0) sb.AppendLine("What is on their mind right now: " + string.Join(" | ", feelings));

            var others = Workers().Where(o => o != h).Select(o => $"{o.GetFullName()} ({Feeling(Safe(() => h.ISocialBehaviour.Social.GetAffectionTowards(o)))})").ToList();
            if (others.Count > 0) sb.AppendLine("Feelings towards fellow villagers: " + string.Join(", ", others));

            if (detailed)
            {
                var life = Safe(() => h.LifeEventLogs.Where(l => l.Type != LifeEventType.Conversation).Take(12).Select(l => StripTags(l.GetLocalizedLogWithoutSprites())).Reverse().ToList());
                if (life?.Count > 0) sb.AppendLine("Recent life events (oldest first): " + string.Join(" / ", life));
            }
            return sb.ToString();
        }

        private static string StateLine(HumanoidInstance h)
        {
            var parts = new List<string>();
            float mood = Stat(h, StatType.Mood);
            parts.Add($"mood {Mathf.RoundToInt(mood)}/100 ({(mood < 20 ? "miserable, close to breaking" : mood < 35 ? "unhappy" : mood < 55 ? "so-so" : mood < 75 ? "content" : "cheerful")})");
            void Need(StatType t, string low)
            {
                float v = Stat(h, t);
                if (v >= 0 && v < 30) parts.Add(low);
            }
            Need(StatType.Hunger, "very hungry");
            Need(StatType.Sleep, "exhausted");
            Need(StatType.Health, "badly hurt");
            Need(StatType.Entertaiment, "bored");
            Need(StatType.Social, "lonely");
            Need(StatType.Faith, "spiritually troubled");
            Need(StatType.TemperatureCold, "freezing");
            Need(StatType.TemperatureHot, "overheated");
            if (Safe(() => h.IsInAlteredState)) parts.Add("in an altered state: " + Safe(() => h.CurrentAlteredState?.ToString()));
            string goal = Safe(() => h.GoapAgent.CurrentGoalName);
            if (!string.IsNullOrEmpty(goal)) parts.Add("currently doing: " + Regex.Replace(goal.Replace("Goal", ""), "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant());
            return string.Join(", ", parts);
        }

        internal static string MoodWord(HumanoidInstance h)
        {
            float mood = Stat(h, StatType.Mood);
            return mood < 0 ? "?" : mood < 20 ? "miserable" : mood < 35 ? "unhappy" : mood < 55 ? "so-so" : mood < 75 ? "content" : "cheerful";
        }

        private static float Stat(HumanoidInstance h, StatType t)
        {
            try { return h.Stats.GetStat(t)?.Current ?? -1f; } catch { return -1f; }
        }

        private static string Feeling(float a)
        {
            return a >= 80 ? "close friend" : a >= 40 ? "likes them" : a >= 10 ? "friendly" : a > -10 ? "neutral" : a > -40 ? "dislikes them" : a > -80 ? "strongly dislikes them" : "rival";
        }

        private static string Clean(string s)
        {
            s = StripTags(s);
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }

        internal static string LocOf(object model, HumanoidInstance h)
        {
            if (model == null) return null;
            try
            {
                var prop = model.GetType().GetProperty("LocKeys");
                if (!(prop?.GetValue(model) is IEnumerable keys)) return null;
                var loc = MonoSingleton<LocalizationController>.Instance;
                foreach (var k in keys)
                {
                    if (!(k is LocKeys lk)) continue;
                    string name = h != null ? loc.GetText(lk.Name, h) : loc.GetText(lk.Name);
                    string info = string.IsNullOrEmpty(lk.Info) ? "" : (h != null ? loc.GetText(lk.Info, h) : loc.GetText(lk.Info));
                    return string.IsNullOrEmpty(info) ? name : name + " — " + info;
                }
            }
            catch { }
            return null;
        }

        // ---------- keyboard ownership ----------

        private static bool inputTakenByUs;

        /// <summary>Stops the game reacting to keys/mouse while the player types or hovers our windows.</summary>
        internal static void SetGameInputBlocked(bool block)
        {
            if (!MonoSingleton<InputManager>.IsInstantiated()) { inputTakenByUs = false; return; }
            var im = MonoSingleton<InputManager>.Instance;
            if (block && !inputTakenByUs && im.InputEnabled)
            {
                im.SetInputEnabled(false);
                inputTakenByUs = true;
            }
            else if (!block && inputTakenByUs)
            {
                im.SetInputEnabled(true);
                inputTakenByUs = false;
            }
        }

        internal static T Safe<T>(Func<T> f)
        {
            try { return f(); } catch { return default; }
        }
    }
}
