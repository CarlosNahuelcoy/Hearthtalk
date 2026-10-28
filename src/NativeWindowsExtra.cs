using System;
using System.Linq;
using System.Text;
using NSMedieval.State;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Hearthtalk
{
    // ---------------------------------------------------------------- memories

    internal class MemoryWindow
    {
        private readonly NativeWindow w;
        private readonly RectTransform actionsRow, confirmRow;
        private readonly TMP_Text status;
        private HumanoidInstance target;
        private int seenVersion = -1;
        private bool writing;

        internal MemoryWindow()
        {
            w = NativeKit.CreateWindow("Hearthtalk_Memories", T.Get("Recuerdos", "Memories"));
            if (w == null) return;
            var footer = w.AddFooter(44);
            actionsRow = NativeKit.Row(footer);
            status = NativeKit.CreateText(actionsRow, w.Label, "", 0.8f);
            status.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            NativeKit.CreateButton(actionsRow, T.Get("Escribir el de hoy", "Write today's"), WriteToday);
            NativeKit.CreateButton(actionsRow, T.Get("Olvidar todo", "Forget all"), () => SetConfirm(true));
            confirmRow = NativeKit.Row(footer);
            var q = NativeKit.CreateText(confirmRow, w.Label, T.Get("¿Borrar todos sus recuerdos? No se puede deshacer.", "Delete all their memories? This cannot be undone."), 0.85f);
            q.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            NativeKit.CreateButton(confirmRow, T.Get("Sí, borrar", "Yes, delete"), () => { if (target != null) Memory.Clear(target); SetConfirm(false); });
            NativeKit.CreateButton(confirmRow, T.Get("Cancelar", "Cancel"), () => SetConfirm(false));
            SetConfirm(false);
        }

        internal bool Ok => w != null;
        internal bool Visible { get => w != null && w.Visible; set { if (w != null) w.Visible = value; } }
        internal NativeWindow Window => w;

        internal void Open(HumanoidInstance h)
        {
            if (w == null || h == null) return;
            target = h;
            seenVersion = -1;
            status.text = "";
            SetConfirm(false);
            w.Title.text = T.Get("Recuerdos de ", "Memories of ") + h.GetFullName();
            w.Visible = true;
        }

        private void SetConfirm(bool on)
        {
            actionsRow.gameObject.SetActive(!on);
            confirmRow.gameObject.SetActive(on);
        }

        private void WriteToday()
        {
            if (target == null || writing) return;
            writing = true;
            status.text = T.Get("Escribiendo…", "Writing…");
            Memory.SummarizeNow(target, err => { writing = false; status.text = err == null ? "" : Rich.Plain(err); });
        }

        internal void Tick()
        {
            if (!Visible) return;
            if (target == null || target.HasDisposed) { Visible = false; return; }
            if (Memory.Version != seenVersion)
            {
                seenVersion = Memory.Version;
                var sb = new StringBuilder();
                var list = Memory.Get(target);
                if (list.Count == 0)
                    sb.AppendLine(T.Get($"<i>{Game.FirstName(target)} aún no tiene recuerdos. Cada noche, quien haya hablado ese día escribe uno; los últimos días entran en sus conversaciones.</i>",
                        $"<i>{Game.FirstName(target)} has no memories yet. Every night, whoever talked that day writes one; the latest days go into their conversations.</i>"));
                foreach (var m in list.AsEnumerable().Reverse())
                    sb.AppendLine($"<b><color=#c9b37e>{Rich.Plain(m.Date)}</color></b>\n{Rich.Plain(m.Text)}\n");
                w.Label.text = sb.ToString();
            }
            w.LateTick();
        }
    }

    // ---------------------------------------------------------------- meeting (group chat)

    internal class MeetingWindow
    {
        private readonly NativeWindow w;
        private readonly TMP_InputField input;
        private int seenVersion = -1;

        internal MeetingWindow()
        {
            w = NativeKit.CreateWindow("Hearthtalk_Meeting", T.Get("Reunión", "Meeting"));
            if (w == null) return;
            var footer = w.AddFooter(92);
            var inputRow = NativeKit.Row(footer);
            input = NativeKit.CreateInput(inputRow, T.Get("Habla a todos y pulsa Enter…", "Speak to everyone and press Enter…"), false);
            input.onSubmit.AddListener(_ => Send());
            NativeKit.CreateButton(inputRow, T.Get("Enviar", "Send"), Send);
            var row = NativeKit.Row(footer);
            var hint = NativeKit.CreateText(row, w.Label, T.Get("<size=85%>Selecciona varios colonos (arrastrando o con Mayús) y pulsa «Hablar» para reunirlos.</size>", "<size=85%>Select several colonists (drag or Shift) and press “Talk” to gather them.</size>"), 0.8f);
            hint.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            ChatWindow.PauseButton(row);
            NativeKit.CreateButton(row, T.Get("Reunir de nuevo", "Gather again"), () => Open(Game.SelectedWorker() ?? GroupChat.Members.FirstOrDefault()));
        }

        internal bool Ok => w != null;
        internal bool Visible { get => w != null && w.Visible; set { if (w != null) w.Visible = value; } }
        internal NativeWindow Window => w;

        internal void Open(HumanoidInstance around)
        {
            if (w == null) return;
            GroupChat.Start(around);
            seenVersion = -1;
            w.Title.text = T.Get("Reunión: ", "Meeting: ") + string.Join(", ", GroupChat.Members.Select(Game.FirstName));
            Portraits.Show(w, GroupChat.Members);
            w.Visible = true;
            input.text = "";
            input.ActivateInputField();
        }

        private void Send()
        {
            if (string.IsNullOrWhiteSpace(input.text) || GroupChat.Waiting) return;
            GroupChat.Send(input.text);
            input.text = "";
            input.ActivateInputField();
        }

        internal void Tick()
        {
            if (!Visible) return;
            if (GroupChat.Version != seenVersion)
            {
                seenVersion = GroupChat.Version;
                var sb = new StringBuilder();
                if (GroupChat.Transcript.Count == 0)
                    sb.AppendLine(T.Get("<i>Tienes a la aldea reunida. Lo que digas lo oyen todos; responderán los que tengan algo que decir.</i>", "<i>The villagers are gathered. Everyone hears what you say; those with something to say will answer.</i>"));
                foreach (var t in GroupChat.Transcript)
                {
                    if (t.FromPlayer) sb.AppendLine($"<align=right><b><color=#e9c46a>{T.Get("Tú", "You")}</color></b>\n{Rich.Plain(t.Text)}</align>\n");
                    else
                    {
                        sb.AppendLine($"<b><color={Rich.ColorFor(t.Speaker)}>{Rich.Plain(t.Speaker)}</color></b>\n{Rich.Plain(t.Text)}");
                        if (t.Notes != null) foreach (var n in t.Notes) sb.AppendLine($"<size=85%><color=#a59c8c>   → {Rich.Plain(n)}</color></size>");
                        sb.AppendLine();
                    }
                }
                if (GroupChat.Waiting) sb.AppendLine($"<i><color=#a59c8c>{T.Get("Murmullos…", "Murmuring…")}</color></i>");
                w.Label.text = sb.ToString();
                w.ScrollToEnd();
            }
            w.LateTick();
        }
    }

    // ---------------------------------------------------------------- chronicler

    internal class StorytellerWindow
    {
        private readonly NativeWindow w;
        private readonly TMP_InputField input;
        private readonly RectTransform actionsRow, confirmRow;
        private int seenVersion = -1;

        internal Action OpenPersonality;

        internal StorytellerWindow()
        {
            w = NativeKit.CreateWindow("Hearthtalk_Storyteller", Storyteller.Name);
            if (w == null) return;
            var footer = w.AddFooter(92);
            var inputRow = NativeKit.Row(footer);
            input = NativeKit.CreateInput(inputRow, T.Get("Pregunta al cronista o pídele algo…", "Ask the chronicler, or ask for something…"), false);
            input.onSubmit.AddListener(_ => Send());
            NativeKit.CreateButton(inputRow, T.Get("Enviar", "Send"), Send);
            actionsRow = NativeKit.Row(footer);
            var hint = NativeKit.CreateText(actionsRow, w.Label, "", 0.8f);
            hint.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            hint.text = Cfg.StorytellerCanTriggerEvents.Value
                ? T.Get("<size=85%>Puede traer eventos si se lo pides.</size>", "<size=85%>Can bring events if you ask.</size>")
                : T.Get("<size=85%>Activa «Puede provocar eventos» en Ajustes para que traiga visitas, clima o incursiones.</size>", "<size=85%>Turn on “Can start events” in Settings to let it bring visitors, weather or raids.</size>");
            NativeKit.CreateButton(actionsRow, T.Get("Personalidad", "Personality"), () => OpenPersonality?.Invoke());
            NativeKit.CreateButton(actionsRow, T.Get("Borrar chat", "Clear chat"), () => SetConfirm(true));
            confirmRow = NativeKit.Row(footer);
            var q = NativeKit.CreateText(confirmRow, w.Label, T.Get("¿Borrar la conversación con el cronista?", "Clear the conversation with the chronicler?"), 0.85f);
            q.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            NativeKit.CreateButton(confirmRow, T.Get("Sí, borrar", "Yes, clear"), () => { Storyteller.Clear(); SetConfirm(false); });
            NativeKit.CreateButton(confirmRow, T.Get("Cancelar", "Cancel"), () => SetConfirm(false));
            SetConfirm(false);
        }

        internal bool Ok => w != null;
        internal bool Visible { get => w != null && w.Visible; set { if (w != null) { w.Visible = value; if (value) { seenVersion = -1; input.ActivateInputField(); } } } }
        internal NativeWindow Window => w;

        private void SetConfirm(bool on)
        {
            actionsRow.gameObject.SetActive(!on);
            confirmRow.gameObject.SetActive(on);
        }

        private void Send()
        {
            if (string.IsNullOrWhiteSpace(input.text) || Storyteller.Waiting) return;
            Storyteller.Send(input.text);
            input.text = "";
            input.ActivateInputField();
        }

        internal void Tick()
        {
            if (!Visible) return;
            if (Storyteller.Version != seenVersion)
            {
                seenVersion = Storyteller.Version;
                var sb = new StringBuilder();
                var hist = Storyteller.History();
                if (hist.Count == 0)
                    sb.AppendLine(T.Get("<i>El cronista lo ha visto todo desde que se fundó la aldea. Pregúntale qué piensa, qué se cuenta de alguien, o qué le depara el destino al pueblo.</i>",
                        "<i>The chronicler has seen everything since the village was founded. Ask what they think, what is said about someone, or what fate holds for the village.</i>"));
                foreach (var t in hist)
                {
                    if (t.FromPlayer) sb.AppendLine($"<align=right><b><color=#e9c46a>{T.Get("Tú", "You")}</color></b>\n{Rich.Plain(t.Text)}</align>\n");
                    else
                    {
                        sb.AppendLine($"<b><color=#c9b37e>{Storyteller.Name}</color></b>\n<i>{Rich.Plain(t.Text)}</i>");
                        if (t.Notes != null) foreach (var n in t.Notes) sb.AppendLine($"<size=85%><color=#a59c8c>   → {Rich.Plain(n)}</color></size>");
                        sb.AppendLine();
                    }
                }
                if (Storyteller.Waiting) sb.AppendLine($"<i><color=#a59c8c>{T.Get("El cronista moja la pluma…", "The chronicler dips the quill…")}</color></i>");
                w.Label.text = sb.ToString();
                w.ScrollToEnd();
            }
            w.LateTick();
        }
    }
}
