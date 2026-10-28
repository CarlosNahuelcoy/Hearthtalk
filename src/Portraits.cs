using System.Collections.Generic;
using System.Linq;
using Controller;
using NSEipix.Base;
using NSMedieval.State;
using NSMedieval.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Hearthtalk
{
    /// <summary>Colonist portraits in a window header, taken from the game's own icon renderer (the ones in the colonist list).</summary>
    internal static class Portraits
    {
        private const float Size = 76f;

        internal static Sprite Of(HumanoidInstance h)
        {
            if (h == null || h.HasDisposed || !MonoSingleton<HumanoidIconManager>.IsInstantiated()) return null;
            return Game.Safe(() => MonoSingleton<HumanoidIconManager>.Instance.GetCachedIcon(h));
        }

        /// <summary>Shows the portraits at the top left of the window, over the empty band under the title.</summary>
        internal static void Show(NativeWindow w, IEnumerable<HumanoidInstance> who)
        {
            if (w?.Title == null) return;
            var header = w.Title.transform.parent?.parent ?? w.Title.transform.parent;
            if (header == null) return;
            var strip = header.Find("HearthtalkPortraits") as RectTransform;
            if (strip == null)
            {
                var go = new GameObject("HearthtalkPortraits", typeof(RectTransform));
                strip = (RectTransform)go.transform;
                strip.SetParent(header, false);
                strip.anchorMin = strip.anchorMax = new Vector2(0f, 1f);
                strip.pivot = new Vector2(0f, 1f);
                strip.anchoredPosition = new Vector2(18f, -8f);
                strip.sizeDelta = new Vector2(Size * 6 + 30f, Size);
                go.AddComponent<LayoutElement>().ignoreLayout = true;
                var row = go.AddComponent<HorizontalLayoutGroup>();
                row.spacing = 6f;
                row.childControlWidth = row.childControlHeight = false;
                row.childForceExpandWidth = row.childForceExpandHeight = false;
            }
            foreach (Transform c in strip) Object.Destroy(c.gameObject);
            var list = (who ?? Enumerable.Empty<HumanoidInstance>()).ToList();
            float size = list.Count > 1 ? Size * 0.7f : Size;
            foreach (var h in list)
            {
                var sprite = Of(h);
                if (sprite == null) continue;
                var img = new GameObject("Portrait", typeof(RectTransform)).AddComponent<Image>();
                img.transform.SetParent(strip, false);
                ((RectTransform)img.transform).sizeDelta = new Vector2(size, size);
                img.sprite = sprite;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
        }
    }

    /// <summary>Pauses the game while a chat is open, through the same pause stack the game's own popups use.</summary>
    internal class ChatPause : IPauseGame
    {
        private static readonly ChatPause token = new ChatPause();
        private static GameplayPauseManager registeredWith;

        internal static void Set(bool pause)
        {
            if (!MonoSingleton<GameplayPauseManager>.IsInstantiated()) { registeredWith = null; return; }
            var mgr = MonoSingleton<GameplayPauseManager>.Instance;
            if (registeredWith != null && registeredWith != mgr) registeredWith = null; // a new scene: the old registration is gone
            if (pause && registeredWith == null)
            {
                mgr.Register(token);
                registeredWith = mgr;
            }
            else if (!pause && registeredWith != null)
            {
                mgr.Unregister(token);
                registeredWith = null;
            }
        }
    }
}
