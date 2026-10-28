using System.Collections.Generic;
using NSMedieval.State;
using UnityEngine;

namespace Hearthtalk
{
    /// <summary>A colonist thinks aloud in a bubble when something happens to them.</summary>
    internal static class Monologues
    {
        private static bool inFlight;
        private static float nextAllowed;

        internal static void OnLifeEvent(HumanoidInstance h, string what)
        {
            if (!Cfg.Monologues.Value || inFlight || Time.unscaledTime < nextAllowed || Game.Paused || !Conversations.LLMReady()) return;
            if (Random.value > Cfg.MonologueChance.Value) return;
            inFlight = true;
            nextAllowed = Time.unscaledTime + Mathf.Max(15f, Cfg.ConversationCooldown.Value);

            string prompt =
                $"You are {h.GetFullName()}, a villager in a medieval English village.\n" +
                Game.ColonistContext(h, false) + "\n" +
                $"This just happened to you: {what}\n" +
                $"Write the one short thing you mutter to yourself right now (max 15 words), in {T.StoryLanguage}, in character. Only the words, no quotes, no narration.";
            string key = Game.ColonistKey(h);
            LLMClient.Complete(new List<Msg> { new Msg("user", prompt) }, 80, "THOUGHT_" + Game.FirstName(h), (text, error) =>
            {
                inFlight = false;
                if (error != null || string.IsNullOrWhiteSpace(text) || h.HasDisposed) return;
                text = text.Trim().Trim('"', '«', '»', '*');
                ChatLog.Add(LogKind.Dialogue, h.GetFullName() + " " + T.Get("(piensa)", "(thinks)"), text, key);
                if (Cfg.ShowBubbles.Value) Bubbles.Say(h, "« " + text + " »", 0f, Cfg.BubbleSecondsPerLine.Value);
            });
        }
    }
}
