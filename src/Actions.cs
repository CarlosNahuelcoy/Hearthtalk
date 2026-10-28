using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using NSEipix.Base;
using NSEipix.Repository;
using NSMedieval;
using NSMedieval.Goap;
using NSMedieval.Repository;
using NSMedieval.State;
using NSMedieval.State.WorkerJobs;
using NSMedieval.StatsSystem;
using UnityEngine;

namespace Hearthtalk
{
    /// <summary>
    /// What a conversation can change in the game. The AI ends its reply with tags such as [MOOD:+2] or [WORK:farming:1];
    /// they are stripped from the text and applied with limits, the same way the game's own panels would.
    /// </summary>
    internal static class Actions
    {
        // ---------- jobs the AI may talk about ----------

        internal static readonly (string id, JobType type, string es, string en)[] Jobs =
        {
            ("hauling", JobType.Hauling, "Acarreo", "Hauling"),
            ("urgent_hauling", JobType.UrgentHaul, "Acarreo urgente", "Urgent hauling"),
            ("mining", JobType.Mining, "Minería", "Mining"),
            ("construction", JobType.Construction, "Construcción", "Construction"),
            ("crafting", JobType.Crafting, "Artesanía", "Crafting"),
            ("harvesting", JobType.Harvesting, "Cosecha", "Harvesting"),
            ("farming", JobType.PlantCropfields, "Cultivo", "Farming"),
            ("woodcutting", JobType.PlantCutting, "Tala", "Woodcutting"),
            ("hunting", JobType.Hunting, "Caza", "Hunting"),
            ("fishing", JobType.Fishing, "Pesca", "Fishing"),
            ("smithing", JobType.Smithing, "Herrería", "Smithing"),
            ("carpentry", JobType.Carpentry, "Carpintería", "Carpentry"),
            ("cooking", JobType.Cooking, "Cocina", "Cooking"),
            ("tailoring", JobType.Tailoring, "Sastrería", "Tailoring"),
            ("research", JobType.Research, "Investigación", "Research"),
            ("doctor", JobType.TendWounds, "Curar heridos", "Tending wounds"),
            ("animals", JobType.Animal, "Animales", "Animal handling"),
            ("art", JobType.Art, "Arte", "Art"),
            ("alchemy", JobType.Alchemy, "Alquimia", "Alchemy"),
            ("gaoler", JobType.Gaoler, "Carcelero", "Gaoler"),
            ("firefighting", JobType.FireFight, "Apagar fuegos", "Firefighting"),
            ("training", JobType.Train, "Entrenamiento", "Training"),
        };

        private static readonly Dictionary<string, HourType> hourTypes = new Dictionary<string, HourType>
        {
            { "work", HourType.Working }, { "sleep", HourType.Sleep }, { "leisure", HourType.Leisure }, { "any", HourType.Any },
        };

        internal static string JobName(JobType t)
        {
            var j = Jobs.FirstOrDefault(x => x.type == t);
            return j.id == null ? t.ToString() : T.Get(j.es, j.en);
        }

        // ---------- mood effectors ----------

        private static readonly (string id, int value, int hours, string es, string esInfo, string en, string enInfo)[] Moods =
        {
            ("Hearthtalk_Mood_P1", 3, 24, "Una buena charla con el señor", "El señor le habló con amabilidad.", "A kind word from the lord", "The lord spoke to them kindly."),
            ("Hearthtalk_Mood_P2", 6, 36, "Animado por el señor", "Las palabras del señor le levantaron el ánimo.", "Encouraged by the lord", "The lord's words lifted their spirits."),
            ("Hearthtalk_Mood_P3", 10, 48, "Inspirado por el señor", "Algo que dijo el señor le llegó al corazón.", "Inspired by the lord", "Something the lord said touched their heart."),
            ("Hearthtalk_Mood_N1", -3, 24, "Palabras duras del señor", "El señor le habló con frialdad.", "Harsh words from the lord", "The lord was cold with them."),
            ("Hearthtalk_Mood_N2", -6, 36, "Reprendido por el señor", "El señor le regañó.", "Scolded by the lord", "The lord scolded them."),
            ("Hearthtalk_Mood_N3", -10, 48, "Humillado por el señor", "El señor le insultó o amenazó.", "Humiliated by the lord", "The lord insulted or threatened them."),
        };

        private static float nextRegisterTry;
        private static bool registered;

        /// <summary>Called every frame: adds our effectors to the game's repository as soon as it is ready (and again if it reloads).</summary>
        internal static void EnsureRegistered()
        {
            if (Time.unscaledTime < nextRegisterTry) return;
            nextRegisterTry = Time.unscaledTime + 2f;
            try
            {
                if (!MonoSingleton<RepositoryManager>.IsInstantiated() || !Repository<EffectorRepository, StatEffector>.IsInstantiated()) return;
                var repo = Repository<EffectorRepository, StatEffector>.Instance;
                if (repo.GetCount() == 0) return;
                if (repo.GetByID(Moods[0].id) != null) { registered = true; return; }

                var sb = new StringBuilder("{\"repository\":[");
                for (int i = 0; i < Moods.Length; i++)
                {
                    var m = Moods[i];
                    string key = m.id.ToLowerInvariant();
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"id\":\"").Append(m.id).Append("\",\"locKeys\":[{\"languageName\":1,\"name\":\"").Append(key).Append("_name\",\"info\":\"").Append(key)
                      .Append("_info\"}],\"category\":\"Hearthtalk\",\"bubbleIcon\":\"").Append(m.value > 0 ? "default_mood_positive" : "default_mood_negative")
                      .Append("\",\"uiGroup\":1,\"duration\":").Append(m.hours).Append(",\"effects\":[{\"devName\":\"").Append(m.id)
                      .Append("Mood\",\"type\":\"MoodModify\",\"parameters\":[{\"key\":\"BaseValue\",\"value\":\"").Append(m.value).Append("\"}]}]}");
                }
                sb.Append("]}");
                string path = Path.Combine(HearthtalkPlugin.DataDir, "Effectors.json");
                File.WriteAllText(path, sb.ToString());
                var rm = MonoSingleton<RepositoryManager>.Instance;
                rm.AddRepository("Effectors.json", path);
                rm.RefreshRepositories();
                registered = Repository<EffectorRepository, StatEffector>.Instance.GetByID(Moods[0].id) != null;
                if (registered) HearthtalkPlugin.Log.LogInfo("mood effectors registered");
            }
            catch (Exception e)
            {
                nextRegisterTry = Time.unscaledTime + 30f;
                HearthtalkPlugin.Log.LogWarning("effector registration failed: " + e.Message);
            }
        }

        /// <summary>Saves made with the mod keep our effectors; make sure they exist before the game restores them.</summary>
        internal static void BeforeSavedEffectorsFire()
        {
            if (registered && Repository<EffectorRepository, StatEffector>.Instance.GetByID(Moods[0].id) != null) return;
            nextRegisterTry = 0f;
            EnsureRegistered();
        }

        internal static bool TryGetText(string key, out string text)
        {
            text = null;
            if (!key.StartsWith("hearthtalk_mood_", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (var m in Moods)
            {
                string k = m.id.ToLowerInvariant();
                if (key.Equals(k + "_name", StringComparison.OrdinalIgnoreCase)) { text = T.Get(m.es, m.en); return true; }
                if (key.Equals(k + "_info", StringComparison.OrdinalIgnoreCase)) { text = T.Get(m.esInfo, m.enInfo); return true; }
            }
            return false;
        }

        // ---------- prompt ----------

        internal static string PromptSection(HumanoidInstance h)
        {
            if (!Cfg.AllowMood.Value && !Cfg.AllowOrders.Value) return "";
            var sb = new StringBuilder();
            sb.AppendLine("WHAT YOUR WORDS CAN CHANGE (optional; most replies need no tag):");
            sb.AppendLine("You may end your reply with one or more tags, each on its own line. They are invisible to the lord; never mention or explain them.");
            if (Cfg.AllowMood.Value)
            {
                sb.AppendLine("[MOOD:+1] [MOOD:+2] [MOOD:+3] — the talk left you feeling better (kindness, praise, gratitude, comfort, a promise kept; +3 only for something deeply moving).");
                sb.AppendLine("[MOOD:-1] [MOOD:-2] [MOOD:-3] — the talk hurt you (coldness, unfair blame, insults, threats, cruelty; -3 only for something truly humiliating).");
                sb.AppendLine("Ordinary small talk gets no MOOD tag.");
            }
            if (Cfg.AllowOrders.Value)
            {
                sb.AppendLine("[WORK:job:1-5] — you AGREE to change how much you focus on a job (1 = above everything, 3 = normal, 5 = only when nothing else).");
                sb.AppendLine("[WORK_STOP:job] — you agree to stop doing that job.   [WORK_START:job] — you agree to take up that job.");
                sb.AppendLine("[HOURS:from-to:work|sleep|leisure|any] — you agree to change your daily schedule for those hours (0-23, e.g. [HOURS:20-23:leisure]).");
                sb.AppendLine("Jobs: " + string.Join(", ", Jobs.Select(j => j.id)) + ".");
                sb.AppendLine("Only use WORK/HOURS tags when the lord asks or orders it AND you accept. You may refuse, bargain or grumble if it goes against your nature, skills, health or mood; if you refuse, use no tag and say why. You cannot do jobs you are unable or refuse to do.");
            }
            return sb.ToString();
        }

        /// <summary>Current jobs and schedule, so the colonist knows what they are being asked to change.</summary>
        internal static string WorkContext(HumanoidInstance h)
        {
            var jb = Game.Safe(() => h.IJobBehaviour);
            var jobs = jb?.Jobs;
            if (jobs == null) return null;
            var active = new List<string>();
            var inactive = new List<string>();
            foreach (var j in Jobs)
            {
                if ((jobs.AllowedJobTypes & j.type) == 0) continue;
                if (jobs.IsJobActive(j.type)) active.Add($"{j.id} {Game.Safe(() => jb.GetJobPriorityTruncated(j.type))}");
                else inactive.Add(j.id);
            }
            var sb = new StringBuilder();
            if (active.Count > 0) sb.AppendLine("Jobs you do now (job priority, 1 = first): " + string.Join(", ", active));
            if (inactive.Count > 0) sb.AppendLine("Jobs you do not do now: " + string.Join(", ", inactive));
            var hours = ScheduleOf(jobs);
            if (hours != null && hours.Length == 24) sb.AppendLine("Daily schedule: " + Schedule(hours));
            return sb.ToString();
        }

        // ScheduleHours is a ReadOnlySpan, which this .NET 4.7.2 build cannot use; read the array behind it.
        private static HourType[] ScheduleOf(HumanoidJobComponent jobs) => Game.Safe(() => Traverse.Create(jobs).Field("scheduleHours").GetValue<HourType[]>());

        private static string Schedule(HourType[] hours)
        {
            var parts = new List<string>();
            int start = 0;
            for (int i = 1; i <= 24; i++)
            {
                if (i < 24 && hours[i] == hours[start]) continue;
                parts.Add($"{start}-{i - 1} {HourName(hours[start])}");
                start = i;
            }
            return string.Join(", ", parts);
        }

        private static string HourName(HourType t)
        {
            switch (t)
            {
                case HourType.Working: return "work";
                case HourType.Sleep: return "sleep";
                case HourType.Leisure: return "leisure";
                case HourType.Any: return "any";
                default: return t.ToString().ToLowerInvariant();
            }
        }

        // ---------- parsing and applying ----------

        private static readonly Regex TagRx = new Regex(@"\[\s*(MOOD|WORK_STOP|WORK_START|WORK|HOURS)\s*:\s*([^\]]*)\]", RegexOptions.IgnoreCase);

        /// <summary>Removes the tags from the reply and applies them. Returns one line per change for the chat.</summary>
        internal static string Apply(HumanoidInstance h, string reply, out List<string> notes)
        {
            notes = new List<string>();
            if (string.IsNullOrEmpty(reply)) return reply;
            var found = TagRx.Matches(reply).Cast<Match>().ToList();
            string clean = TagRx.Replace(reply, "");
            clean = Regex.Replace(clean, @"[ \t]+\n", "\n").Trim();
            if (found.Count == 0 || h == null || h.HasDisposed) return clean;

            bool moodDone = false;
            foreach (var m in found)
            {
                string kind = m.Groups[1].Value.ToUpperInvariant();
                string[] args = m.Groups[2].Value.Split(':').Select(a => a.Trim()).ToArray();
                string note = null;
                try
                {
                    switch (kind)
                    {
                        case "MOOD":
                            if (!Cfg.AllowMood.Value || moodDone) break;
                            moodDone = true;
                            note = ApplyMood(h, args[0]);
                            break;
                        case "WORK":
                            if (Cfg.AllowOrders.Value && args.Length >= 2 && int.TryParse(args[1], out int p)) note = SetJob(h, args[0], Mathf.Clamp(p, 1, 5), false);
                            break;
                        case "WORK_START":
                            if (Cfg.AllowOrders.Value) note = SetJob(h, args[0], 3, false, onlyIfInactive: true);
                            break;
                        case "WORK_STOP":
                            if (Cfg.AllowOrders.Value) note = SetJob(h, args[0], 0, true);
                            break;
                        case "HOURS":
                            if (Cfg.AllowOrders.Value && args.Length >= 2) note = SetHours(h, args[0], args[1]);
                            break;
                    }
                }
                catch (Exception e) { HearthtalkPlugin.Log.LogWarning($"action {kind} failed: {e.Message}"); }
                if (note != null)
                {
                    notes.Add(note);
                    ChatLog.Add(LogKind.System, Game.FirstName(h), note, Game.ColonistKey(h));
                }
            }
            return clean;
        }

        private static readonly Dictionary<int, double> lastMoodHour = new Dictionary<int, double>();

        private static string ApplyMood(HumanoidInstance h, string arg)
        {
            if (!int.TryParse(arg.Replace("+", ""), out int tier) || tier == 0) return null;
            tier = Mathf.Clamp(tier, -3, 3);
            double now = Game.TotalHours();
            if (lastMoodHour.TryGetValue(h.UniqueId, out double last) && now - last < Cfg.MoodCooldownHours.Value && now >= last)
            {
                HearthtalkPlugin.Log.LogInfo($"mood change for {Game.FirstName(h)} skipped: cooldown");
                return null;
            }
            EnsureRegistered();
            var m = Moods[tier > 0 ? tier - 1 : 2 - tier];
            foreach (var other in Moods) if (Game.Safe(() => h.Stats.IsEffectorActive(other.id))) h.Stats.EndEffector(other.id);
            if (!h.Stats.StartEffector(m.id)) return null;
            lastMoodHour[h.UniqueId] = now;
            return T.Get($"Ánimo {(m.value > 0 ? "+" : "")}{m.value}: «{m.es}»", $"Mood {(m.value > 0 ? "+" : "")}{m.value}: “{m.en}”");
        }

        private static string SetJob(HumanoidInstance h, string jobId, int priority, bool stop, bool onlyIfInactive = false)
        {
            var j = Jobs.FirstOrDefault(x => x.id.Equals(jobId.Replace(" ", "_"), StringComparison.OrdinalIgnoreCase)
                || x.en.Equals(jobId, StringComparison.OrdinalIgnoreCase) || x.es.Equals(jobId, StringComparison.OrdinalIgnoreCase));
            if (j.id == null) return null;
            var jb = h.IJobBehaviour;
            var jobs = jb?.Jobs;
            if (jobs == null || (jobs.AllowedJobTypes & j.type) == 0) return null;
            bool active = jobs.IsJobActive(j.type);
            int current = jb.GetJobPriorityTruncated(j.type);
            if (stop)
            {
                if (!active) return null;
                jb.ModifyJobPriority(j.type, 0, true);
                return T.Get($"Deja de hacer: {JobName(j.type)}", $"Stops doing: {JobName(j.type)}");
            }
            if (onlyIfInactive && active) return null;
            if (active && current == priority) return null;
            jb.ModifyJobPriority(j.type, priority - current, false);
            return T.Get($"{JobName(j.type)}: prioridad {priority}", $"{JobName(j.type)}: priority {priority}");
        }

        private static string SetHours(HumanoidInstance h, string range, string type)
        {
            if (!hourTypes.TryGetValue(type.ToLowerInvariant(), out var hourType)) return null;
            var bits = range.Split('-');
            if (!int.TryParse(bits[0], out int from)) return null;
            int to = bits.Length > 1 && int.TryParse(bits[1], out int t) ? t : from;
            from = Mathf.Clamp(from, 0, 23); to = Mathf.Clamp(to, 0, 23);
            var jobs = h.IJobBehaviour?.Jobs;
            if (jobs == null || jobs.IsScheduleReadOnly) return null;
            var hours = ScheduleOf(jobs);
            int changed = 0;
            for (int i = from; ; i = (i + 1) % 24)
            {
                if (hours == null || hours[i] != HourType.RoleJob) { h.ChangeSchedule(i, hourType); changed++; }
                if (i == to || changed > 24) break;
            }
            if (changed == 0) return null;
            string name = T.Get(hourType == HourType.Working ? "trabajo" : hourType == HourType.Sleep ? "sueño" : hourType == HourType.Leisure ? "ocio" : "libre",
                hourType == HourType.Working ? "work" : hourType == HourType.Sleep ? "sleep" : hourType == HourType.Leisure ? "leisure" : "anything");
            return T.Get($"Horario {from}:00–{to}:59 → {name}", $"Schedule {from}:00–{to}:59 → {name}");
        }
    }

    [HarmonyPatch(typeof(StatsInstance), nameof(StatsInstance.FireSavedEffectors))]
    internal static class Patch_FireSavedEffectors
    {
        private static void Prefix()
        {
            try { Actions.BeforeSavedEffectorsFire(); }
            catch (Exception e) { HearthtalkPlugin.Log.LogWarning("effector check before load failed: " + e.Message); }
        }
    }
}
