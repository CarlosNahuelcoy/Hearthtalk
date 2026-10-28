using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using NSMedieval.State;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hearthtalk
{
    internal static class Rich
    {
        private static readonly string[] palette = { "#e9c46a", "#f4a261", "#81b29a", "#8ecae6", "#e5989b", "#cdb4db", "#a3b18a", "#ffb703", "#90be6d", "#f28482" };

        internal static string ColorFor(string name)
        {
            if (string.IsNullOrEmpty(name)) return "#cccccc";
            int h = 0;
            foreach (char c in name) h = h * 31 + c;
            return palette[Math.Abs(h) % palette.Length];
        }

        /// <summary>Text from players or the AI never gets to inject TMP tags.</summary>
        internal static string Plain(string s) => "<noparse>" + (s ?? "").Replace("</noparse>", "") + "</noparse>";
    }

    // ---------------------------------------------------------------- live log

    internal class LogWindow
    {
        private readonly NativeWindow w;
        private readonly TMP_Text status;
        private readonly Button filterButton;
        private bool aiOnly;
        private int seenVersion = -1;
        private float nextStatus;

        internal Action OpenSettings, OpenMeeting, OpenStoryteller;

        internal LogWindow()
        {
            w = NativeKit.CreateWindow("Hearthtalk_LiveLog", T.Get("Conversaciones en vivo", "Live conversations"));
            if (w == null) return;
            var footer = w.AddFooter(44);
            var row = NativeKit.Row(footer);
            status = NativeKit.CreateText(row, w.Label, "", 0.8f);
            status.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            filterButton = NativeKit.CreateButton(row, FilterText(), () => { aiOnly = !aiOnly; NativeKit.SetButtonText(filterButton, FilterText()); seenVersion = -1; });
            NativeKit.CreateButton(row, T.Get("Reunión", "Meeting"), () => OpenMeeting?.Invoke());
            NativeKit.CreateButton(row, T.Get("Cronista", "Chronicler"), () => OpenStoryteller?.Invoke());
            NativeKit.CreateButton(row, T.Get("Ajustes", "Settings"), () => OpenSettings?.Invoke());
        }

        internal bool Ok => w != null;
        internal bool Visible { get => w != null && w.Visible; set { if (w != null) { w.Visible = value; seenVersion = -1; } } }
        internal NativeWindow Window => w;

        private string FilterText() => aiOnly ? T.Get("Mostrar todo", "Show all") : T.Get("Solo diálogos IA", "AI dialogue only");

        internal void Tick()
        {
            if (!Visible) return;
            if (ChatLog.Version != seenVersion)
            {
                bool atEnd = w.Scroll == null || w.Scroll.verticalNormalizedPosition < 0.03f || seenVersion < 0;
                seenVersion = ChatLog.Version;
                w.Label.text = Build();
                if (atEnd) w.ScrollToEnd();
            }
            if (Time.unscaledTime > nextStatus)
            {
                nextStatus = Time.unscaledTime + 0.5f;
                status.text = StatusText();
            }
            w.LateTick();
        }

        private string Build()
        {
            var sb = new StringBuilder();
            var entries = ChatLog.Entries;
            string lastTime = null;
            foreach (var e in entries.Skip(Math.Max(0, entries.Count - 300)))
            {
                if (e.Kind == LogKind.Player || e.Kind == LogKind.Reply || e.Kind == LogKind.System) continue;
                if (aiOnly && e.Kind == LogKind.Native) continue;
                if (e.Kind == LogKind.Native) { sb.AppendLine($"<color=#8f887d><size=85%><i>[{e.Time}] {Rich.Plain(e.Text)}</i></size></color>"); continue; }
                if (e.Kind == LogKind.Narrator) { sb.AppendLine($"\n<color=#c9b37e><i>{Rich.Plain(e.Text)}</i></color>"); continue; }
                if (e.Time != lastTime) { sb.AppendLine($"\n<color=#8c8273><size=80%>— {e.Time} —</size></color>"); lastTime = e.Time; }
                string color = e.Kind == LogKind.Player ? "#e9c46a" : Rich.ColorFor(e.Speaker);
                sb.AppendLine($"<b><color={color}>{Rich.Plain(e.Speaker)}:</color></b> {Rich.Plain(e.Text)}");
            }
            if (sb.Length == 0)
                sb.Append(T.Get("Aún no hay conversaciones. Los colonos charlan solos cuando están ociosos y cerca unos de otros; cada diálogo aparecerá aquí.",
                    "No conversations yet. Colonists chat on their own when idle and near each other; every dialogue will show up here."));
            return sb.ToString();
        }

        private static string StatusText()
        {
            string provider = Conversations.LLMReady()
                ? (Cfg.ConversationsEnabled.Value ? "" : T.Get("IA de charlas apagada · ", "Chat AI off · "))
                : T.Get("<color=#e07a5f>Sin proveedor de IA</color> · ", "<color=#e07a5f>No AI provider</color> · ");
            string counts = T.Get($"Charlas detectadas: {Conversations.Seen} · con IA: {Conversations.Generated} · fallos: {Conversations.Failed}",
                $"Chats detected: {Conversations.Seen} · with AI: {Conversations.Generated} · failed: {Conversations.Failed}");
            string err = string.IsNullOrEmpty(Conversations.LastError) ? "" : $"\n<color=#e07a5f>Error: {Rich.Plain(Conversations.LastError)}</color>";
            return "<size=85%>" + provider + counts + err + "</size>";
        }
    }

    // ---------------------------------------------------------------- one-to-one chat

    internal class ChatWindow
    {
        private readonly NativeWindow w;
        private readonly TMP_InputField input;
        private readonly RectTransform actionsRow, confirmRow, editRow;
        private readonly TMP_Text confirmText, editText;
        private HumanoidInstance target;
        private int seenVersion = -1, seenCount = -1;
        private bool seenWaiting;
        private Action confirmAction;
        private int editIndex = -1; // message being edited, -1 when writing a new one

        internal Action<HumanoidInstance> OpenPersona, OpenMemories, OpenMeeting;

        internal ChatWindow()
        {
            w = NativeKit.CreateWindow("Hearthtalk_Chat", T.Get("Hablar", "Talk"));
            if (w == null) return;
            var footer = w.AddFooter(92);

            var inputRow = NativeKit.Row(footer);
            input = NativeKit.CreateInput(inputRow, T.Get("Escribe y pulsa Enter…", "Type and press Enter…"), false);
            input.onSubmit.AddListener(_ => Send());
            NativeKit.CreateButton(inputRow, T.Get("Enviar", "Send"), Send);

            actionsRow = NativeKit.Row(footer);
            NativeKit.CreateButton(actionsRow, T.Get("Personalidad", "Personality"), () => { if (target != null) OpenPersona?.Invoke(target); });
            NativeKit.CreateButton(actionsRow, T.Get("Recuerdos", "Memories"), () => { if (target != null) OpenMemories?.Invoke(target); });
            NativeKit.CreateButton(actionsRow, T.Get("Reunión", "Meeting"), () => { if (target != null) OpenMeeting?.Invoke(target); });
            PauseButton(actionsRow);
            NativeKit.CreateButton(actionsRow, T.Get("Borrar chat", "Clear chat"), () =>
            {
                if (target == null) return;
                Confirm(T.Get($"¿Borrar toda la conversación con {Game.FirstName(target)}? No se puede deshacer.", $"Clear the whole chat with {Game.FirstName(target)}? This cannot be undone."),
                    () => { DirectChat.Clear(target); seenVersion = -1; });
            });

            confirmRow = NativeKit.Row(footer);
            confirmText = NativeKit.CreateText(confirmRow, w.Label, "", 0.85f);
            confirmText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            NativeKit.CreateButton(confirmRow, T.Get("Sí, borrar", "Yes, delete"), () => { var a = confirmAction; ShowRow(actionsRow); a?.Invoke(); });
            NativeKit.CreateButton(confirmRow, T.Get("Cancelar", "Cancel"), () => ShowRow(actionsRow));

            editRow = NativeKit.Row(footer);
            editText = NativeKit.CreateText(editRow, w.Label, "", 0.85f);
            editText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            NativeKit.CreateButton(editRow, T.Get("Cancelar edición", "Cancel edit"), StopEditing);
            ShowRow(actionsRow);

            // The template's link handler sends clicks to the almanac; ours handles the message actions.
            foreach (var old in w.Label.GetComponents<NSMedieval.Almanac.LinkTextHandler>()) UnityEngine.Object.DestroyImmediate(old);
            w.Label.raycastTarget = true;
            w.Label.gameObject.AddComponent<TextLinks>().Clicked = OnLink;
        }

        internal bool Ok => w != null;
        internal bool Visible { get => w != null && w.Visible; set { if (w != null) w.Visible = value; } }
        internal HumanoidInstance Target => target;
        internal NativeWindow Window => w;

        internal void Open(HumanoidInstance h)
        {
            if (w == null || h == null) return;
            target = h;
            seenVersion = -1;
            StopEditing();
            w.Title.text = T.Get("Hablar con ", "Talk to ") + h.GetFullName();
            Portraits.Show(w, new[] { h });
            w.Visible = true;
            input.text = "";
            input.ActivateInputField();
            PrivateMessages.MarkRead(h);
        }

        /// <summary>One click pauses or resumes the game while the chat is open; the choice is remembered.</summary>
        internal static Button PauseButton(Transform row)
        {
            Button b = null;
            string Label() => Cfg.PauseWhileChatting.Value ? T.Get("Pausa: Sí", "Pause: On") : T.Get("Pausa: No", "Pause: Off");
            b = NativeKit.CreateButton(row, Label(), () => { Cfg.PauseWhileChatting.Value = !Cfg.PauseWhileChatting.Value; Cfg.Save(); NativeKit.SetButtonText(b, Label()); });
            return b;
        }

        private void ShowRow(RectTransform row)
        {
            actionsRow.gameObject.SetActive(row == actionsRow);
            confirmRow.gameObject.SetActive(row == confirmRow);
            editRow.gameObject.SetActive(row == editRow);
        }

        private void Confirm(string question, Action action)
        {
            confirmText.text = question;
            confirmAction = action;
            ShowRow(confirmRow);
        }

        private void StopEditing()
        {
            if (editIndex >= 0) input.text = "";
            editIndex = -1;
            ShowRow(actionsRow);
            seenVersion = -1;
        }

        private void Send()
        {
            if (target == null || string.IsNullOrWhiteSpace(input.text) || DirectChat.IsWaiting(target)) return;
            var hist = DirectChat.History(target);
            if (editIndex >= 0 && editIndex < hist.Count)
            {
                if (hist[editIndex].FromPlayer) DirectChat.EditPlayerMessage(target, editIndex, input.text);
                else DirectChat.EditReply(target, editIndex, input.text);
                editIndex = -1;
                ShowRow(actionsRow);
            }
            else DirectChat.Send(target, input.text);
            input.text = "";
            input.ActivateInputField();
            w.ScrollToEnd();
        }

        /// <summary>Links under each message: e = edit, d = delete, r = regenerate.</summary>
        private void OnLink(string id)
        {
            if (target == null || DirectChat.IsWaiting(target) || id.Length < 3 || !int.TryParse(id.Substring(2), out int i)) return;
            var hist = DirectChat.History(target);
            if (i < 0 || i >= hist.Count) return;
            switch (id[0])
            {
                case 'r':
                    DirectChat.Regenerate(target);
                    break;
                case 'e':
                    editIndex = i;
                    input.text = hist[i].Text;
                    editText.text = hist[i].FromPlayer
                        ? T.Get("Editando tu mensaje. Al enviarlo, lo que vino después se borra y te vuelve a responder.", "Editing your message. When sent, everything after it is removed and they answer again.")
                        : T.Get($"Editando lo que dijo {Game.FirstName(target)}. Se guarda tal cual, sin pedir otra respuesta.", $"Editing what {Game.FirstName(target)} said. It is saved as written, with no new answer.");
                    ShowRow(editRow);
                    input.ActivateInputField();
                    input.MoveTextEnd(false);
                    seenVersion = -1;
                    break;
                case 'd':
                    bool mine = hist[i].FromPlayer;
                    Confirm(mine ? T.Get("¿Borrar tu mensaje y su respuesta?", "Delete your message and its answer?") : T.Get("¿Borrar este mensaje?", "Delete this message?"),
                        () => { DirectChat.Delete(target, i); if (editIndex >= 0) StopEditing(); });
                    break;
            }
        }

        internal void Tick()
        {
            if (!Visible) return;
            if (target == null || target.HasDisposed) { Visible = false; return; }
            var hist = DirectChat.History(target);
            bool waiting = DirectChat.IsWaiting(target);
            if (DirectChat.Version != seenVersion || hist.Count != seenCount || waiting != seenWaiting)
            {
                if (hist.Count != seenCount) PrivateMessages.MarkRead(target);
                // Editing or deleting an old message should not jump to the bottom; new lines should.
                bool scroll = hist.Count > seenCount || waiting != seenWaiting;
                seenVersion = DirectChat.Version;
                seenCount = hist.Count;
                seenWaiting = waiting;
                w.Label.text = Build(hist, waiting);
                if (scroll) w.ScrollToEnd();
            }
            w.LateTick();
        }

        private string Build(List<ChatTurn> hist, bool waiting)
        {
            var sb = new StringBuilder();
            string name = Game.FirstName(target), color = Rich.ColorFor(target.GetFullName());
            string persona = Personas.Get(target);
            if (hist.Count == 0)
            {
                sb.AppendLine(T.Get($"<i>Empieza a hablar con {name}. Conoce su historia, su ánimo, sus necesidades y lo que ha vivido en la aldea.</i>",
                    $"<i>Start talking to {name}. They know their story, mood, needs and what they have lived through in the village.</i>"));
                if (string.IsNullOrEmpty(persona))
                    sb.AppendLine(T.Get("<i><size=85%>Con «Personalidad» puedes escribir cómo es y cómo habla.</size></i>", "<i><size=85%>Use “Personality” to write who they are and how they speak.</size></i>"));
            }
            int lastReply = hist.FindLastIndex(t => !t.FromPlayer);
            bool canRegen = DirectChat.CanRegenerate(target);
            for (int i = 0; i < hist.Count; i++)
            {
                var t = hist[i];
                bool editing = i == editIndex;
                string mark = editing ? $"<color=#e9c46a>{T.Get("(editando)", "(editing)")} </color>" : "";
                if (t.FromPlayer)
                {
                    sb.AppendLine($"<align=right>{mark}<b><color=#e9c46a>{T.Get("Tú", "You")}</color></b>\n{Rich.Plain(t.Text)}");
                    if (!waiting) sb.AppendLine(Links(i, edit: true, delete: true, regen: false));
                    sb.AppendLine("</align>");
                }
                else
                {
                    string tag = t.Unprompted ? $" <size=80%><color=#a59c8c>{T.Get("vino a hablarte", "came to talk to you")} · {t.Time}</color></size>" : "";
                    string body = t.IsError ? $"<color=#e07a5f>{Rich.Plain(t.Text)}</color>" : Rich.Plain(t.Text);
                    sb.AppendLine($"{mark}<b><color={color}>{name}</color></b>{tag}\n{body}");
                    if (t.Notes != null) foreach (var n in t.Notes) sb.AppendLine($"<size=85%><color=#a59c8c>   → {Rich.Plain(n)}</color></size>");
                    if (!waiting) sb.AppendLine(Links(i, edit: !t.IsError, delete: true, regen: i == lastReply && canRegen));
                    sb.AppendLine();
                }
            }
            if (waiting) sb.AppendLine($"<i><color=#a59c8c>{name} {T.Get("está pensando…", "is thinking…")}</color></i>");
            return sb.ToString();
        }

        private static string Links(int i, bool edit, bool delete, bool regen)
        {
            var parts = new List<string>();
            if (regen) parts.Add(Link("r", i, T.Get("regenerar", "regenerate")));
            if (edit) parts.Add(Link("e", i, T.Get("editar", "edit")));
            if (delete) parts.Add(Link("d", i, T.Get("borrar", "delete")));
            return "<size=78%>" + string.Join("<color=#5f5a52>  ·  </color>", parts) + "</size>";
        }

        private static string Link(string kind, int i, string text) => $"<link=\"{kind}:{i}\"><color=#8fb3c9>{text}</color></link>";
    }

    /// <summary>Turns clicks on &lt;link&gt; tags of a TMP text into callbacks.</summary>
    internal class TextLinks : MonoBehaviour, UnityEngine.EventSystems.IPointerClickHandler
    {
        internal Action<string> Clicked;

        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData e)
        {
            var text = GetComponent<TMP_Text>();
            if (text == null) return;
            var canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            int link = TMP_TextUtilities.FindIntersectingLink(text, e.position, cam);
            if (link < 0 || link >= text.textInfo.linkCount) return;
            Clicked?.Invoke(text.textInfo.linkInfo[link].GetLinkID());
        }
    }

    // ---------------------------------------------------------------- personality editor

    internal class PersonaWindow
    {
        private readonly NativeWindow w;
        private readonly TMP_Text intro;
        private readonly TMP_InputField input;
        private HumanoidInstance target;
        private Action<string> onSave;

        internal PersonaWindow()
        {
            w = NativeKit.CreateWindow("Hearthtalk_Persona", T.Get("Personalidad", "Personality"));
            if (w == null) return;
            var rows = w.UseRows();
            intro = NativeKit.CreateText(rows, w.Label, "", 0.9f);
            input = NativeKit.CreateInput(rows, T.Get("Ej.: Gruñón pero leal. Habla poco y con refranes. Odia el frío y desconfía de los forasteros.", "E.g. Grumpy but loyal. Speaks little, in proverbs. Hates the cold and distrusts strangers."), true, 2000);
            ((LayoutElement)input.GetComponent<LayoutElement>()).minHeight = 220;
            var footer = w.AddFooter(44);
            var row = NativeKit.Row(footer);
            NativeKit.CreateButton(row, T.Get("Guardar", "Save"), () => { onSave?.Invoke(input.text); w.Visible = false; });
            NativeKit.CreateButton(row, T.Get("Quitar", "Remove"), () => { onSave?.Invoke(""); input.text = ""; });
            NativeKit.CreateButton(row, T.Get("Cancelar", "Cancel"), () => w.Visible = false);
        }

        internal bool Ok => w != null;
        internal bool Visible { get => w != null && w.Visible; set { if (w != null) w.Visible = value; } }
        internal NativeWindow Window => w;

        internal void Open(HumanoidInstance h)
        {
            if (w == null || h == null) return;
            target = h;
            w.Title.text = T.Get("Personalidad de ", "Personality of ") + h.GetFullName();
            intro.text = T.Get(
                $"Describe cómo es {Game.FirstName(h)}: carácter, manera de hablar, secretos, manías, lo que ama u odia. Tiene prioridad sobre todo lo demás, tanto en el chat como en sus charlas con otros colonos. Déjalo vacío para usar solo lo que dice el juego.",
                $"Describe who {Game.FirstName(h)} is: temperament, way of speaking, secrets, quirks, loves and hates. It overrides everything else, both in chat and in their talks with other colonists. Leave empty to use only what the game says.");
            input.text = Personas.Get(h);
            onSave = text => Personas.Set(h, text);
            w.Visible = true;
            input.ActivateInputField();
        }

        /// <summary>Any other free text: the village prompt, the chronicler's personality.</summary>
        internal void OpenText(string title, string introText, string value, Action<string> save)
        {
            if (w == null) return;
            target = null;
            w.Title.text = title;
            intro.text = introText;
            input.text = value ?? "";
            onSave = save;
            w.Visible = true;
            input.ActivateInputField();
        }

        internal void Tick() { if (Visible) w.LateTick(); }
    }

    // ---------------------------------------------------------------- settings

    internal class SettingsWindow
    {
        private readonly NativeWindow w;
        private RectTransform rows;
        private TMP_Text testResult, p2Status;
        private float nextStatus;

        internal SettingsWindow()
        {
            w = NativeKit.CreateWindow("Hearthtalk_Settings", "Hearthtalk — " + T.Get("Ajustes", "Settings"));
            if (w == null) return;
            rows = w.UseRows();
            var footer = w.AddFooter(44);
            var row = NativeKit.Row(footer);
            testResult = NativeKit.CreateText(row, w.Label, "", 0.8f);
            testResult.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            NativeKit.CreateButton(row, T.Get("Probar conexión", "Test connection"), Test);
            NativeKit.CreateButton(row, T.Get("Guardar", "Save"), () => { Cfg.Save(); testResult.text = T.Get("Guardado.", "Saved."); });
            Rebuild();
        }

        internal bool Ok => w != null;
        internal bool Visible { get => w != null && w.Visible; set { if (w != null) { w.Visible = value; if (!value) Cfg.Save(); } } }
        internal NativeWindow Window => w;

        internal void Tick()
        {
            if (!Visible) return;
            if (p2Status != null && Time.unscaledTime > nextStatus)
            {
                nextStatus = Time.unscaledTime + 0.5f;
                p2Status.text = Player2Line();
            }
            w.LateTick();
        }

        private void Rebuild()
        {
            foreach (Transform c in rows) UnityEngine.Object.Destroy(c.gameObject);
            p2Status = null;

            Header(T.Get("Proveedor de IA", "AI provider"));
            var prov = NativeKit.Row(rows, 6);
            var names = new[] { "Player2", "Gemini", "OpenRouter", T.Get("Local / OpenAI", "Local / OpenAI") };
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var b = NativeKit.CreateButton(prov, (int)Cfg.Provider.Value == i ? "» " + names[i] + " «" : names[i], () =>
                {
                    Cfg.Provider.Value = (LLMProvider)idx;
                    Cfg.Save();
                    if (Cfg.Provider.Value == LLMProvider.Player2) Player2Auth.TryAutoConnect();
                    Rebuild();
                });
                b.interactable = (int)Cfg.Provider.Value != i;
            }

            switch (Cfg.Provider.Value)
            {
                case LLMProvider.Player2:
                    Text(T.Get("Gratis con tu cuenta de Player2. Si la app de Player2 está abierta se conecta sola.", "Free with your Player2 account. Connects by itself when the Player2 app is open."));
                    p2Status = Text(Player2Line());
                    Buttons((T.Get("Conectar con la app", "Connect with the app"), () => Player2Auth.ConnectLocal()),
                        (T.Get("Conectar con el navegador", "Connect with the browser"), Player2Auth.ConnectBrowser),
                        (T.Get("Desconectar", "Disconnect"), Player2Auth.Disconnect));
                    break;
                case LLMProvider.Gemini:
                    Field("API key", Cfg.GeminiKey, true);
                    Field(T.Get("Modelo", "Model"), Cfg.GeminiModel, false);
                    Buttons((T.Get("Conseguir una API key gratis", "Get a free API key"), () => Application.OpenURL("https://aistudio.google.com/apikey")));
                    break;
                case LLMProvider.OpenRouter:
                    Field("API key", Cfg.OpenRouterKey, true);
                    Field(T.Get("Modelo", "Model"), Cfg.OpenRouterModel, false);
                    break;
                default:
                    Text(T.Get("LM Studio, Ollama, KoboldCpp o cualquier servidor compatible con /v1/chat/completions.", "LM Studio, Ollama, KoboldCpp or any /v1/chat/completions server."));
                    Field("URL", Cfg.CustomEndpoint, false);
                    Field(T.Get("Modelo", "Model"), Cfg.CustomModel, false);
                    Field("API key", Cfg.CustomKey, true);
                    break;
            }

            Header(T.Get("Charlas entre colonos", "Colonist chats"));
            Toggle(T.Get("Convertir sus charlas en diálogos reales con IA", "Turn their chats into real AI dialogue"), Cfg.ConversationsEnabled);
            Toggle(T.Get("Burbujas de diálogo sobre los colonos", "Speech bubbles over colonists"), Cfg.ShowBubbles);
            Toggle(T.Get("Anotar en el registro las charlas sin IA", "List non-AI chats in the live log"), Cfg.LogNativeConversations);
            Stepper(T.Get("Pausa mínima entre diálogos IA (s)", "Minimum gap between AI dialogues (s)"), Cfg.ConversationCooldown, 5f, 5f, 300f, "0");
            StepperInt(T.Get("Líneas por diálogo", "Lines per dialogue"), Cfg.ConversationLines, 2, 8);
            Stepper(T.Get("Duración de cada burbuja (s)", "Bubble duration (s)"), Cfg.BubbleSecondsPerLine, 1f, 2f, 15f, "0");

            Header(T.Get("Mensajes privados", "Private messages"));
            Toggle(T.Get("Los colonos te escriben por su cuenta", "Colonists write to you on their own"), Cfg.PrivateMessages);
            Toggle(T.Get("Cuando pasa algo (incursiones, visitas, clima…)", "When something happens (raids, visitors, weather…)"), Cfg.PrivateOnEvents);
            Toggle(T.Get("Cuando les pasa algo (heridas, peleas…)", "When something happens to them (injuries, fights…)"), Cfg.PrivateOnLifeEvents);
            Toggle(T.Get("Cuando están mal (hambre, tristeza, agotamiento)", "When they are struggling (hunger, misery, exhaustion)"), Cfg.PrivateOnNeeds);
            Stepper(T.Get("Probabilidad por hora de juego de un mensaje al azar", "Chance per game hour of a random message"), Cfg.PrivateRandomChance, 0.01f, 0f, 0.5f, "0.00");
            Stepper(T.Get("Minutos reales mínimos entre mensajes", "Minimum real minutes between messages"), Cfg.PrivateMinMinutes, 1f, 1f, 60f, "0");
            Stepper(T.Get("Horas de juego antes de que el mismo colono vuelva a escribir", "Game hours before the same colonist writes again"), Cfg.PrivatePerColonistHours, 6f, 6f, 168f, "0");

            Header(T.Get("Influencia", "Influence"));
            Toggle(T.Get("Tus palabras cambian su ánimo", "Your words change their mood"), Cfg.AllowMood);
            Stepper(T.Get("Horas de juego entre cambios de ánimo por colono", "Game hours between mood changes per colonist"), Cfg.MoodCooldownHours, 2f, 0f, 72f, "0");
            Toggle(T.Get("Aceptan órdenes de trabajo y horario (pueden negarse)", "They accept job and schedule orders (they may refuse)"), Cfg.AllowOrders);

            Header(T.Get("Memoria y pensamientos", "Memory and thoughts"));
            Toggle(T.Get("Recuerdo diario de cada colono", "Daily memory for each colonist"), Cfg.MemoryEnabled);
            StepperInt(T.Get("Días que recuerdan en cada conversación", "Days they remember in each conversation"), Cfg.MemoryDaysInPrompt, 1, 30);
            Toggle(T.Get("Piensan en voz alta cuando les pasa algo", "They think aloud when something happens"), Cfg.Monologues);
            Stepper(T.Get("Probabilidad de pensar en voz alta", "Chance of thinking aloud"), Cfg.MonologueChance, 0.05f, 0f, 1f, "0.00");

            Header(T.Get("Cronista", "Chronicler"));
            Toggle(T.Get("Activar el cronista", "Enable the chronicler"), Cfg.StorytellerEnabled);
            Toggle(T.Get("Comenta los eventos en el registro", "Comments on events in the log"), Cfg.StorytellerComments);
            Toggle(T.Get("Puede provocar eventos si se lo pides", "Can start events when you ask"), Cfg.StorytellerCanTriggerEvents);

            Header(T.Get("Datos", "Data"));
            Buttons((T.Get("Exportar a texto", "Export to text"), () => testResult.text = Rich.Plain(DataPorter.Export())),
                (T.Get("Abrir carpeta", "Open folder"), DataPorter.OpenFolder));
            Text(T.Get("Importar de otra aldea (copia chats, personalidades y recuerdos de colonos con el mismo nombre):", "Import from another village (copies chats, personalities and memories of colonists with the same name):"));
            var others = DataPorter.OtherVillages();
            if (others.Count == 0) Text("<i>" + T.Get("No hay otras aldeas guardadas.", "No other villages saved.") + "</i>");
            else
            {
                var row = NativeKit.Row(rows, 6);
                foreach (var v in others.Take(6))
                {
                    string name = v;
                    NativeKit.CreateButton(row, name, () => testResult.text = Rich.Plain(DataPorter.ImportFrom(name)));
                }
            }

            Header(T.Get("Escritura", "Writing"));
            Field(T.Get("Idioma (auto = el del juego)", "Language (auto = game's)"), Cfg.Language, false);
            Stepper(T.Get("Creatividad", "Creativity"), Cfg.Temperature, 0.1f, 0f, 1.5f, "0.0");
            Text(T.Get("Instrucciones extra para la IA (tono, estilo, límites):", "Extra instructions for the AI (tone, style, limits):"));
            var gp = NativeKit.CreateInput(rows, T.Get("Ej.: Diálogos breves y con humor seco. Sin palabrotas.", "E.g. Short dialogue with dry humour. No swearing."), true, 2000);
            gp.text = Cfg.GlobalPrompt.Value ?? "";
            gp.onEndEdit.AddListener(v => { Cfg.GlobalPrompt.Value = v; Cfg.Save(); });

            Header(T.Get("Interfaz", "Interface"));
            var lang = NativeKit.Row(rows, 6);
            foreach (var (value, label) in new[] { ("English", "English"), ("Spanish", "Español") })
            {
                bool current = T.Spanish == (value == "Spanish");
                var b = NativeKit.CreateButton(lang, current ? "» " + label + " «" : label, () =>
                {
                    Cfg.InterfaceLanguage.Value = value;
                    Cfg.Save();
                    Rebuild();
                    testResult.text = T.Get("Las demás ventanas cambian al reiniciar el juego.", "Other windows switch after restarting the game.");
                });
                b.interactable = !current;
            }
            Toggle(T.Get("Pausar el juego mientras hablas con colonos", "Pause the game while talking to colonists"), Cfg.PauseWhileChatting);
            Toggle(T.Get("Guardar prompts y respuestas en debug.log", "Save prompts and replies to debug.log"), Cfg.DebugLog);
        }

        private string Player2Line()
        {
            string state = Player2Auth.IsConnected ? "<color=#81b29a>" + T.Get("Conectado", "Connected") + "</color>" : "<color=#e07a5f>" + T.Get("No conectado", "Not connected") + "</color>";
            string code = string.IsNullOrEmpty(Player2Auth.UserCode) ? "" : "\n" + T.Get("Código: ", "Code: ") + "<size=130%><b>" + Player2Auth.UserCode + "</b></size>";
            return state + "  " + Rich.Plain(Player2Auth.Status) + code;
        }

        private void Test()
        {
            Cfg.Save();
            testResult.text = T.Get("Probando…", "Testing…");
            var msgs = new List<Msg> { new Msg("system", $"Answer in {T.StoryLanguage} with one short medieval greeting."), new Msg("user", "Hello!") };
            LLMClient.Complete(msgs, 60, "TEST", (text, err) =>
            {
                if (testResult != null)
                    testResult.text = err == null ? "<color=#81b29a>OK: " + Rich.Plain(text) + "</color>" : "<color=#e07a5f>Error: " + Rich.Plain(err) + "</color>";
            });
        }

        // ----- row builders

        private void Header(string text)
        {
            var t = NativeKit.CreateText(rows, w.Title ?? w.Label, "<b>" + text.ToUpperInvariant() + "</b>", w.Title != null ? 0.75f : 1f);
            t.margin = new Vector4(0, 10, 0, 0);
        }

        private TMP_Text Text(string text) => NativeKit.CreateText(rows, w.Label, text, 0.85f);

        private void Buttons(params (string text, Action action)[] buttons)
        {
            var row = NativeKit.Row(rows, 6);
            foreach (var (text, action) in buttons) NativeKit.CreateButton(row, text, action);
        }

        private void Field(string label, ConfigEntry<string> entry, bool secret)
        {
            var row = NativeKit.Row(rows);
            var l = NativeKit.CreateText(row, w.Label, label, 0.9f);
            l.gameObject.AddComponent<LayoutElement>().preferredWidth = 220;
            var input = NativeKit.CreateInput(row, "", false, 500);
            if (secret) { input.contentType = TMP_InputField.ContentType.Password; input.asteriskChar = '•'; }
            input.text = entry.Value ?? "";
            input.onEndEdit.AddListener(v => { entry.Value = v.Trim(); Cfg.Save(); });
        }

        private void Toggle(string label, ConfigEntry<bool> entry)
        {
            var row = NativeKit.Row(rows);
            var l = NativeKit.CreateText(row, w.Label, label, 0.9f);
            l.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Button b = null;
            b = NativeKit.CreateButton(row, OnOff(entry.Value), () => { entry.Value = !entry.Value; Cfg.Save(); NativeKit.SetButtonText(b, OnOff(entry.Value)); }, 110);
        }

        private static string OnOff(bool v) => v ? T.Get("Sí", "On") : T.Get("No", "Off");

        private void Stepper(string label, ConfigEntry<float> entry, float step, float min, float max, string fmt)
        {
            var row = NativeKit.Row(rows);
            var l = NativeKit.CreateText(row, w.Label, label, 0.9f);
            l.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            TMP_Text value = null;
            NativeKit.CreateButton(row, "-", () => { entry.Value = Mathf.Clamp(entry.Value - step, min, max); value.text = entry.Value.ToString(fmt); Cfg.Save(); }, 44);
            value = NativeKit.CreateText(row, w.Label, entry.Value.ToString(fmt), 0.9f);
            value.alignment = TextAlignmentOptions.Center;
            value.gameObject.AddComponent<LayoutElement>().preferredWidth = 60;
            NativeKit.CreateButton(row, "+", () => { entry.Value = Mathf.Clamp(entry.Value + step, min, max); value.text = entry.Value.ToString(fmt); Cfg.Save(); }, 44);
        }

        private void StepperInt(string label, ConfigEntry<int> entry, int min, int max)
        {
            var row = NativeKit.Row(rows);
            var l = NativeKit.CreateText(row, w.Label, label, 0.9f);
            l.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            TMP_Text value = null;
            NativeKit.CreateButton(row, "-", () => { entry.Value = Mathf.Clamp(entry.Value - 1, min, max); value.text = entry.Value.ToString(); Cfg.Save(); }, 44);
            value = NativeKit.CreateText(row, w.Label, entry.Value.ToString(), 0.9f);
            value.alignment = TextAlignmentOptions.Center;
            value.gameObject.AddComponent<LayoutElement>().preferredWidth = 60;
            NativeKit.CreateButton(row, "+", () => { entry.Value = Mathf.Clamp(entry.Value + 1, min, max); value.text = entry.Value.ToString(); Cfg.Save(); }, 44);
        }
    }
}
