using System;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using NSEipix.Base;
using NSEipix.View.UI;
using NSMedieval;
using NSMedieval.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Hearthtalk
{
    /// <summary>
    /// Builds windows out of the game's own UI: the life-log popup is cloned for the frame, title, close button and
    /// scroll area; its hidden text button and the sign's text field serve as templates for our controls.
    /// </summary>
    internal static class NativeKit
    {
        private static SoundButton buttonTemplate;
        private static TMP_InputField inputTemplate;
        private static bool dumped;

        internal static bool Available =>
            MonoSingleton<UIController>.IsInstantiated() && MonoSingleton<UIController>.Instance.LogPanelView != null;

        internal static NativeWindow CreateWindow(string name, string title)
        {
            if (!Available) return null;
            var tpl = MonoSingleton<UIController>.Instance.LogPanelView;
            var tr = Traverse.Create(tpl);
            var tContent = tr.Field("content").GetValue<GameObject>();
            var tTitle = tr.Field("title").GetValue<TMP_Text>();
            var tLabel = tr.Field("logLabel").GetValue<TMP_Text>();
            var tCloses = tr.Field("closeButtons").GetValue<SoundButton[]>() ?? new SoundButton[0];
            var tButton = tr.Field("devGenerateButton").GetValue<SoundButton>();
            var tToggles = tr.Field("toggleParent").GetValue<Component>();
            if (!dumped) { dumped = true; Dump(tpl.transform); }

            // Instantiate under an inactive holder so the clone's LogPanelView never wakes up.
            var holder = new GameObject("HearthtalkHolder");
            holder.SetActive(false);
            var clone = UnityEngine.Object.Instantiate(tpl.gameObject, holder.transform);
            clone.name = name;
            UnityEngine.Object.DestroyImmediate(clone.GetComponent<LogPanelView>());
            Transform Map(Component c) => c == null ? null : Find(clone.transform, PathOf(tpl.transform, c.transform));

            var w = new NativeWindow
            {
                Root = clone,
                Content = Map(tContent?.transform)?.gameObject ?? clone,
                Title = Map(tTitle)?.GetComponent<TMP_Text>(),
                Label = Map(tLabel)?.GetComponent<TMP_Text>(),
            };
            if (buttonTemplate == null && tButton != null) buttonTemplate = tButton;
            var toggles = Map(tToggles);
            if (toggles != null) toggles.gameObject.SetActive(false);
            var dev = Map(tButton);
            if (dev != null) dev.gameObject.SetActive(false);
            foreach (var c in tCloses)
            {
                var b = Map(c)?.GetComponent<SoundButton>();
                if (b == null) continue;
                b.onClick = new Button.ButtonClickedEvent();
                b.onClick.AddListener(() => w.Visible = false);
                // The native footer button reads "Close" even in other languages.
                foreach (var t in b.GetComponentsInChildren<TMP_Text>(true))
                {
                    foreach (var loc in t.GetComponents<I2.Loc.Localize>()) UnityEngine.Object.DestroyImmediate(loc);
                    t.text = T.Get("Cerrar", "Close");
                }
            }
            // Native popups blur and block the whole screen; ours stay out of the way so the village stays visible and clickable.
            var blur = w.Content.transform.Find("Blur");
            if (blur != null) blur.gameObject.SetActive(false);
            // Only our own texts lose their translator; the native "Close" keeps translating itself.
            foreach (var t in new[] { w.Title, w.Label })
                if (t != null) foreach (var loc in t.GetComponents<I2.Loc.Localize>()) UnityEngine.Object.DestroyImmediate(loc);

            clone.transform.SetParent(tpl.transform.parent, false);
            UnityEngine.Object.Destroy(holder);
            clone.SetActive(true);
            w.Content.SetActive(false);

            w.Scroll = w.Label != null ? w.Label.GetComponentInParent<ScrollRect>(true) : clone.GetComponentInChildren<ScrollRect>(true);
            // The template lays a second frame image ("Scroll View_01") over the scroll area; its shading darkens the
            // lower lines of text. Moving it behind the scroll view keeps the frame look and leaves the text evenly lit.
            if (w.Scroll != null && w.Scroll.transform.parent != null)
            {
                var scrollTr = w.Scroll.transform;
                foreach (Transform sib in scrollTr.parent.Cast<Transform>().ToList())
                    if (sib != scrollTr && sib.GetSiblingIndex() > scrollTr.GetSiblingIndex() && sib.GetComponent<Image>() != null && sib.GetComponent<ScrollRect>() == null)
                        sib.SetSiblingIndex(scrollTr.GetSiblingIndex());
            }
            if (w.Title != null)
            {
                w.Title.text = title;
                w.Title.raycastTarget = true;
                w.Title.gameObject.AddComponent<WindowDrag>().Window = w;
            }
            if (w.Label != null)
            {
                w.Label.text = "";
                w.Label.richText = true;
            }
            return w;
        }

        internal static Button CreateButton(Transform parent, string text, Action onClick, float minWidth = 90f)
        {
            GameObject go;
            if (buttonTemplate != null)
            {
                go = UnityEngine.Object.Instantiate(buttonTemplate.gameObject, parent, false);
                go.SetActive(true);
                foreach (var loc in go.GetComponentsInChildren<I2.Loc.Localize>(true)) UnityEngine.Object.DestroyImmediate(loc);
                foreach (var tip in go.GetComponentsInChildren<LocalizedTextTooltipView>(true)) UnityEngine.Object.DestroyImmediate(tip);
            }
            else
            {
                go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(parent, false);
                go.GetComponent<Image>().color = new Color(0.2f, 0.3f, 0.35f, 0.9f);
                var t = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                t.transform.SetParent(go.transform, false);
                Stretch((RectTransform)t.transform);
                t.alignment = TextAlignmentOptions.Center;
                t.fontSize = 18;
            }
            go.name = "Hearthtalk_" + text;
            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => onClick());
            var label = go.GetComponentInChildren<TMP_Text>(true);
            if (label != null) { label.text = text; label.enableWordWrapping = false; }
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            float pref = label != null ? label.GetPreferredValues(text).x + 36f : minWidth;
            le.minWidth = Mathf.Max(minWidth, pref);
            le.preferredWidth = le.minWidth;
            le.minHeight = le.preferredHeight = 38f;
            le.flexibleWidth = 0;
            return button;
        }

        internal static void SetButtonText(Button b, string text)
        {
            var label = b.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = text;
        }

        internal static TMP_InputField CreateInput(Transform parent, string placeholder, bool multiline, int limit = 600)
        {
            var tpl = InputTemplate();
            GameObject go;
            if (tpl != null)
            {
                go = UnityEngine.Object.Instantiate(tpl.gameObject, parent, false);
                go.SetActive(true);
                foreach (var loc in go.GetComponentsInChildren<I2.Loc.Localize>(true)) UnityEngine.Object.DestroyImmediate(loc);
            }
            else
                go = BuildPlainInput(parent);
            go.name = "Hearthtalk_Input";
            var input = go.GetComponent<TMP_InputField>();
            input.onValueChanged = new TMP_InputField.OnChangeEvent();
            input.onEndEdit = new TMP_InputField.SubmitEvent();
            input.onSubmit = new TMP_InputField.SubmitEvent();
            input.onSelect = new TMP_InputField.SelectionEvent();
            input.onDeselect = new TMP_InputField.SelectionEvent();
            input.characterLimit = limit;
            input.interactable = true;
            input.readOnly = false;
            input.lineType = multiline ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
            input.text = "";
            if (input.placeholder is TMP_Text ph) ph.text = placeholder;
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            le.flexibleWidth = 1;
            le.minHeight = le.preferredHeight = multiline ? 140f : 40f;
            return input;
        }

        private static TMP_InputField InputTemplate()
        {
            if (inputTemplate != null) return inputTemplate;
            var sign = Resources.FindObjectsOfTypeAll<SignExtraPanel>().FirstOrDefault(s => s.gameObject.scene.IsValid()) ?? Resources.FindObjectsOfTypeAll<SignExtraPanel>().FirstOrDefault();
            inputTemplate = sign != null ? Traverse.Create(sign).Field("messageInput").GetValue<TMP_InputField>() : null;
            if (inputTemplate == null)
                inputTemplate = Resources.FindObjectsOfTypeAll<TMP_InputField>().FirstOrDefault(i => i.gameObject.scene.IsValid() && i.textComponent != null);
            return inputTemplate;
        }

        private static GameObject BuildPlainInput(Transform parent)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f);
            var area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            area.transform.SetParent(go.transform, false);
            var art = (RectTransform)area.transform;
            Stretch(art);
            art.offsetMin = new Vector2(8, 4); art.offsetMax = new Vector2(-8, -4);
            var text = new GameObject("Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            text.transform.SetParent(area.transform, false);
            Stretch((RectTransform)text.transform);
            text.fontSize = 18;
            var ph = new GameObject("Placeholder", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            ph.transform.SetParent(area.transform, false);
            Stretch((RectTransform)ph.transform);
            ph.fontSize = 18; ph.fontStyle = FontStyles.Italic; ph.color = new Color(1, 1, 1, 0.4f);
            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = art; input.textComponent = text; input.placeholder = ph;
            return go;
        }

        /// <summary>A text line styled like the window's body text.</summary>
        internal static TMP_Text CreateText(Transform parent, TMP_Text style, string text, float sizeFactor = 1f)
        {
            var t = new GameObject("Hearthtalk_Text", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            t.transform.SetParent(parent, false);
            if (style != null)
            {
                t.font = style.font;
                t.fontSharedMaterial = style.fontSharedMaterial;
                t.fontSize = style.fontSize * sizeFactor;
                t.color = style.color;
            }
            else t.fontSize = 18 * sizeFactor;
            t.richText = true;
            t.enableWordWrapping = true;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        internal static RectTransform Row(Transform parent, float spacing = 8f)
        {
            var go = new GameObject("Hearthtalk_Row", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;
            return (RectTransform)go.transform;
        }

        internal static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static string PathOf(Transform root, Transform t)
        {
            if (t == null) return null;
            var parts = new System.Collections.Generic.List<string>();
            while (t != null && t != root) { parts.Insert(0, t.name); t = t.parent; }
            return t == root ? string.Join("/", parts) : null;
        }

        private static Transform Find(Transform root, string path)
        {
            if (path == null) return null;
            return path.Length == 0 ? root : root.Find(path);
        }

        /// <summary>Writes the structure of the template popup to help adjust layouts after game updates.</summary>
        private static void Dump(Transform root)
        {
            try
            {
                var sb = new StringBuilder();
                void Walk(Transform t, int depth)
                {
                    var rt = t as RectTransform;
                    string comps = string.Join(", ", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name));
                    string geo = rt == null ? "" : $" anchors({rt.anchorMin.x:0.##},{rt.anchorMin.y:0.##})-({rt.anchorMax.x:0.##},{rt.anchorMax.y:0.##}) size({rt.rect.width:0},{rt.rect.height:0}) pos({rt.anchoredPosition.x:0},{rt.anchoredPosition.y:0})";
                    string img = t.GetComponent<Image>() is Image i ? $" sprite={i.sprite?.name} type={i.type} color={i.color}" : "";
                    string txt = t.GetComponent<TMP_Text>() is TMP_Text tx ? $" font={tx.font?.name} size={tx.fontSize} text='{(tx.text ?? "").Replace("\n", " ").Substring(0, Math.Min(40, (tx.text ?? "").Length))}'" : "";
                    sb.AppendLine($"{new string(' ', depth * 2)}{t.name}{(t.gameObject.activeSelf ? "" : " [off]")} : {comps}{geo}{img}{txt}");
                    foreach (Transform c in t) Walk(c, depth + 1);
                }
                Walk(root, 0);
                var input = InputTemplate();
                if (input != null) { sb.AppendLine("\n=== input template"); Walk(input.transform, 0); }
                File.WriteAllText(Path.Combine(HearthtalkPlugin.DataDir, "ui-dump.txt"), sb.ToString());
            }
            catch (Exception e) { HearthtalkPlugin.Log.LogWarning("ui dump failed: " + e.Message); }
        }
    }

    internal class NativeWindow
    {
        public GameObject Root;
        public GameObject Content;
        public TMP_Text Title;
        public TMP_Text Label;
        public ScrollRect Scroll;
        private bool scrollToEnd;

        /// <summary>The visible frame: the popup content itself, or its biggest child when the content is a full-screen backdrop.</summary>
        public RectTransform Frame
        {
            get
            {
                var rt = (RectTransform)Content.transform;
                var canvas = Content.GetComponentInParent<Canvas>();
                var full = canvas != null ? ((RectTransform)canvas.transform).rect.size : new Vector2(Screen.width, Screen.height);
                if (rt.rect.width < full.x * 0.95f || rt.rect.height < full.y * 0.95f) return rt;
                var child = rt.Cast<Transform>().OfType<RectTransform>().Where(c => c.gameObject.activeSelf && c.GetComponent<Image>() != null)
                    .OrderByDescending(c => c.rect.width * c.rect.height).FirstOrDefault(c => c.rect.width < full.x * 0.95f);
                return child ?? rt;
            }
        }

        public bool Visible
        {
            get => Root != null && Content != null && Content.activeSelf;
            set
            {
                if (Root == null || Content == null) return;
                Content.SetActive(value);
                if (value) { Root.transform.SetAsLastSibling(); ScrollToEnd(); }
            }
        }

        public void ScrollToEnd() => scrollToEnd = true;

        /// <summary>Called every frame from the UI controller.</summary>
        public void LateTick()
        {
            if (!scrollToEnd || Scroll == null || !Visible) return;
            Canvas.ForceUpdateCanvases();
            Scroll.verticalNormalizedPosition = 0f;
            scrollToEnd = false;
        }

        /// <summary>Makes room under the scroll area for a footer and returns it.</summary>
        public RectTransform AddFooter(float height)
        {
            var scrollRt = Scroll != null ? (RectTransform)Scroll.transform : (RectTransform)Frame;
            var parent = scrollRt.parent;
            var go = new GameObject("Hearthtalk_Footer", typeof(RectTransform));
            var footer = (RectTransform)go.transform;
            footer.SetParent(parent, false);
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = 6;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            if (parent.GetComponent<LayoutGroup>() != null)
            {
                footer.SetSiblingIndex(scrollRt.GetSiblingIndex() + 1);
                var le = go.AddComponent<LayoutElement>();
                le.minHeight = le.preferredHeight = height;
            }
            else
            {
                footer.anchorMin = new Vector2(scrollRt.anchorMin.x, scrollRt.anchorMin.y);
                footer.anchorMax = new Vector2(scrollRt.anchorMax.x, scrollRt.anchorMin.y);
                footer.pivot = new Vector2(0.5f, 0f);
                footer.offsetMin = new Vector2(scrollRt.offsetMin.x, scrollRt.offsetMin.y);
                footer.offsetMax = new Vector2(scrollRt.offsetMax.x, scrollRt.offsetMin.y + height);
                scrollRt.offsetMin += new Vector2(0, height + 8);
            }
            return footer;
        }

        /// <summary>Replaces the single text label with a vertical list of rows (for settings).</summary>
        public RectTransform UseRows()
        {
            if (Scroll == null) return null;
            if (Label != null) Label.gameObject.SetActive(false);
            var parent = Scroll.viewport != null ? Scroll.viewport : (RectTransform)Scroll.transform;
            var go = new GameObject("Hearthtalk_Rows", typeof(RectTransform));
            var rows = (RectTransform)go.transform;
            rows.SetParent(parent, false);
            rows.anchorMin = new Vector2(0, 1); rows.anchorMax = new Vector2(1, 1); rows.pivot = new Vector2(0.5f, 1);
            rows.offsetMin = new Vector2(8, 0); rows.offsetMax = new Vector2(-8, 0);
            var v = go.AddComponent<VerticalLayoutGroup>();
            v.spacing = 8; v.padding = new RectOffset(4, 4, 6, 12);
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Scroll.content = rows;
            return rows;
        }

        public bool InputFocused() =>
            Visible && EventSystem.current != null && EventSystem.current.currentSelectedGameObject is GameObject g
            && g.transform.IsChildOf(Root.transform) && g.GetComponent<TMP_InputField>() is TMP_InputField f && f.isFocused;
    }

    /// <summary>Lets the player drag a window by its title.</summary>
    internal class WindowDrag : MonoBehaviour, IDragHandler, IBeginDragHandler
    {
        public NativeWindow Window;
        private RectTransform target;
        private Canvas canvas;

        public void OnBeginDrag(PointerEventData e)
        {
            canvas = GetComponentInParent<Canvas>();
            target = Window?.Frame;
        }

        public void OnDrag(PointerEventData e)
        {
            if (target == null) return;
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            target.anchoredPosition += e.delta / scale;
        }
    }
}
