using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NSEipix.View.UI;
using NSMedieval.Controllers;
using NSMedieval.Model;
using NSMedieval.State;
using NSMedieval.UI;
using NSMedieval.UI.Utils;
using NSMedieval.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hearthtalk
{
    /// <summary>Puts Hearthtalk inside the game's own UI: a tab in the top bar and a "Talk" action on the colonist panel.</summary>
    internal static class UIIntegration
    {
        internal const string TalkActionId = "Hearthtalk";
        private const string TalkIcon = "Hearthtalk_Talk";
        internal static string TalkIconName => TalkIcon;

        internal static event Action TopButtonClicked;
        internal static event Action<HumanoidInstance> TalkClicked;

        private static SelectionInputActionData talkAction;
        private static Sprite talkSprite;

        // ---------- top bar ----------

        internal static void AddTopButton(TopLeftPanelView panel)
        {
            var objectives = Traverse.Create(panel).Field("objectivesButton").GetValue<SoundButton>();
            if (objectives == null) { HearthtalkPlugin.Log.LogWarning("top bar: objectives button not found"); return; }
            var parent = objectives.transform.parent;
            if (parent.Find("HearthtalkButton") != null) return;

            var go = UnityEngine.Object.Instantiate(objectives.gameObject, parent);
            go.name = "HearthtalkButton";
            go.transform.SetSiblingIndex(objectives.transform.GetSiblingIndex() + 1);

            // The clone would otherwise re-translate itself back to "Objectives".
            foreach (var loc in go.GetComponentsInChildren<I2.Loc.Localize>(true)) UnityEngine.Object.Destroy(loc);
            foreach (var t in go.GetComponentsInChildren<TMP_Text>(true)) t.text = "HEARTHTALK";
            foreach (var tip in go.GetComponentsInChildren<LocalizedTextTooltipView>(true))
            {
                tip.TextKeys = new List<string>();
                tip.SetNonLocalizedText("Hearthtalk\n" + T.Get("Conversaciones de la aldea en vivo y ajustes de la IA.", "Live village conversations and AI settings."));
            }

            var button = go.GetComponent<SoundButton>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => TopButtonClicked?.Invoke());

            // Without a layout group the clone sits on top of the original; move it one button to the right.
            if (parent.GetComponent<LayoutGroup>() == null)
            {
                var src = (RectTransform)objectives.transform;
                var rt = (RectTransform)go.transform;
                rt.anchoredPosition = src.anchoredPosition + new Vector2(src.rect.width + 4f, 0f);
            }
            HearthtalkPlugin.Log.LogInfo("Hearthtalk tab added to the top bar");
        }

        // ---------- colonist panel ----------

        internal static void AddTalkAction(WorkerView view, List<InfoPanelAction> actions)
        {
            var h = view.HumanoidInstance;
            if (actions == null || h == null || h.HasDisposed || !Game.Safe(() => h.IsWorker())) return;
            if (Game.Safe(() => h.WorkerBehaviour.IsBanished || h.IsLeaving)) return;
            EnsureTalkAction();
            actions.Add(new InfoPanelAction(new[] { new KeyValuePair<SelectionInputActionData, Action>(talkAction, () => TalkClicked?.Invoke(h)) }));
        }

        /// <summary>The game clears its sprite cache between scenes, so re-register the icon when it is gone.</summary>
        internal static void EnsureIcon()
        {
            var cache = Traverse.Create(typeof(AssetUtils)).Field("spriteOverrideCache").GetValue<Dictionary<string, Sprite>>();
            if (talkSprite == null) talkSprite = BuildTalkSprite();
            if (cache != null && (!cache.TryGetValue(TalkIcon, out var s) || s == null)) cache[TalkIcon] = talkSprite;
        }

        private static void EnsureTalkAction()
        {
            EnsureIcon();

            if (talkAction != null) return;
            talkAction = (SelectionInputActionData)Activator.CreateInstance(typeof(SelectionInputActionData), true);
            var t = Traverse.Create(talkAction);
            t.Field("id").SetValue(TalkActionId);
            t.Field("iconPath").SetValue(TalkIcon);
            t.Field("locKeys").SetValue(new[] { new LocKeys("English", "hearthtalk_talk", null, null) });
        }

        /// <summary>Texts for our keys: the button label and the two tooltip lines the game builds from the action id.</summary>
        internal static bool TryGetText(string key, out string text)
        {
            text = null;
            if (key == null || key.IndexOf("hearthtalk", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (Actions.TryGetText(key, out text)) return true;
            text = key.IndexOf("info", StringComparison.OrdinalIgnoreCase) >= 0
                ? T.Get("Habla de tú a tú con este colono. Conoce su historia, su ánimo y lo que ha vivido.", "Talk one-to-one with this colonist. They know their story, mood and what they have lived through.")
                : T.Get("Hablar", "Talk");
            return true;
        }

        /// <summary>A white speech bubble with three dots, drawn at runtime so the mod stays a single DLL.</summary>
        private static Sprite BuildTalkSprite()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float cx = 64f, cy = 72f, rx = 50f, ry = 38f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px0 = x + 0.5f, py0 = y + 0.5f;
                    float dx = (px0 - cx) / rx, dy = (py0 - cy) / ry;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float bubble = Mathf.Clamp01((1f - d) * rx * 0.8f);
                    // Tail: a triangle under the bubble, bottom-left.
                    float tail = InTriangle(px0, py0, 34, 44, 58, 40, 24, 12) ? 1f : 0f;
                    float a = Mathf.Max(bubble, tail);
                    // Three dots punched out of the bubble.
                    foreach (var dotX in new[] { 40f, 64f, 88f })
                    {
                        float dd = Mathf.Sqrt((px0 - dotX) * (px0 - dotX) + (py0 - cy) * (py0 - cy));
                        a *= Mathf.Clamp01(dd - 7f);
                    }
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static bool InTriangle(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d1 = (px - bx) * (ay - by) - (ax - bx) * (py - by);
            float d2 = (px - cx) * (by - cy) - (bx - cx) * (py - cy);
            float d3 = (px - ax) * (cy - ay) - (cx - ax) * (py - ay);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }

    [HarmonyPatch(typeof(TopLeftPanelView), "Start")]
    internal static class Patch_TopBar
    {
        private static void Postfix(TopLeftPanelView __instance)
        {
            try { UIIntegration.AddTopButton(__instance); }
            catch (Exception e) { HearthtalkPlugin.Log.LogError("top bar button failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(WorkerView), "GetFooterData")]
    internal static class Patch_WorkerFooter
    {
        private static void Postfix(WorkerView __instance, List<InfoPanelAction> __result)
        {
            try { UIIntegration.AddTalkAction(__instance, __result); }
            catch (Exception e) { HearthtalkPlugin.Log.LogError("talk action failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(LocalizationController), nameof(LocalizationController.GetText), typeof(string))]
    internal static class Patch_LocText
    {
        private static bool Prefix(string key, ref string __result)
        {
            if (!UIIntegration.TryGetText(key, out var text)) return true;
            __result = text;
            return false;
        }
    }
}
