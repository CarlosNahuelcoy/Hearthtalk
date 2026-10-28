using System.Collections.Generic;
using System.Text;
using NSMedieval.FloatingOverlaySystem;
using NSMedieval.State;
using NSMedieval.View;
using UnityEngine;

namespace Hearthtalk
{
    /// <summary>Speech bubbles built on the game's own floating text (the one used for psychotic lines).</summary>
    internal static class Bubbles
    {
        private class Pending { public HumanoidInstance Who; public string Text; public float ShowAt; public float Duration; }
        private class Shown { public HumanoidInstance Who; public FloatingElementBase El; public float Until; }

        private static readonly List<Pending> pending = new List<Pending>();
        private static readonly List<Shown> shown = new List<Shown>();

        internal static void Say(HumanoidInstance who, string text, float delay, float duration)
        {
            pending.Add(new Pending { Who = who, Text = Wrap(text, 42), ShowAt = Time.unscaledTime + delay, Duration = duration });
        }

        internal static void Update()
        {
            float now = Time.unscaledTime;
            for (int i = shown.Count - 1; i >= 0; i--)
            {
                if (now < shown[i].Until) continue;
                Dispose(shown[i].El);
                shown.RemoveAt(i);
            }
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                if (now < p.ShowAt) continue;
                pending.RemoveAt(i);
                if (p.Who == null || p.Who.HasDisposed) continue;
                var view = Game.Safe(() => p.Who.GetAgentView<AnimatedAgentView>());
                if (view == null) continue;
                // One bubble per colonist: a new line replaces the previous one.
                for (int j = shown.Count - 1; j >= 0; j--)
                    if (shown[j].Who == p.Who) { Dispose(shown[j].El); shown.RemoveAt(j); }
                var el = Game.Safe(() => FloatingElementFactory.ProduceTextElement(OverlayTextElementType.Default, FloatingElementHolderType.Default, view.GetGuiOverlayHookTransform(), p.Text));
                if (el != null) shown.Add(new Shown { Who = p.Who, El = el, Until = now + p.Duration });
            }
        }

        internal static void ClearAll()
        {
            pending.Clear();
            foreach (var s in shown) Dispose(s.El);
            shown.Clear();
        }

        private static void Dispose(FloatingElementBase el)
        {
            try { if (el != null && !el.HasDisposed) el.Dispose(); } catch { }
        }

        private static string Wrap(string text, int width)
        {
            var sb = new StringBuilder();
            int line = 0;
            foreach (var word in text.Split(' '))
            {
                if (line > 0 && line + word.Length + 1 > width) { sb.Append('\n'); line = 0; }
                else if (line > 0) { sb.Append(' '); line++; }
                sb.Append(word);
                line += word.Length;
            }
            return sb.ToString();
        }
    }
}
