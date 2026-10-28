using System;
using System.Collections.Generic;
using System.Linq;
using NSMedieval.State;
using UnityEngine;

namespace Hearthtalk
{
    /// <summary>Hearthtalk's windows. Opened from the top bar tab and the colonist's Talk button; the mod takes no hotkeys.</summary>
    internal class HearthUI : MonoBehaviour
    {
        private const int LogId = 0x4ea01, ChatId = 0x4ea02, SettingsId = 0x4ea03;
        private const string InputControl = "HearthtalkInput";

        private bool showLog, showChat, showSettings;
        private Rect logRect = new Rect(60, 120, 520, 460);
        private Rect chatRect = new Rect(600, 120, 500, 520);
        private Rect settingsRect = new Rect(320, 80, 560, 640);
        private Vector2 logScroll, chatScroll, settingsScroll, pickerScroll;
        private bool logStick = true, chatStick = true;
        private int seenLogVersion = -1, seenChatCount = -1;
        private bool aiOnly;
        private string input = "";
        private HumanoidInstance chatTarget;
        private int resizing; // window id being resized
        private float scale = 1f;

        private GUIStyle windowStyle, labelStyle, smallStyle, nameStyle, bubblePlayer, bubbleColonist, buttonStyle, headerStyle, textFieldStyle;
        private Texture2D bgTex, playerTex, colonistTex, headerTex;

        // Native (game-styled) windows; the IMGUI ones below are only a fallback outside a village or if cloning fails.
        private LogWindow nLog;
        private ChatWindow nChat;
        private PersonaWindow nPersona;
        private SettingsWindow nSettings;
        private MemoryWindow nMemory;
        private MeetingWindow nMeeting;
        private StorytellerWindow nStory;
        private string nativeFailedFor;

        private bool Native => nLog != null && nLog.Ok && nLog.Window.Root != null;

        private void EnsureNative()
        {
            if (!Game.InVillage || !NativeKit.Available || Native || nativeFailedFor == Game.VillageKey) return;
            try
            {
                nLog = new LogWindow { OpenSettings = ToggleSettings };
                nChat = new ChatWindow();
                nPersona = new PersonaWindow();
                nSettings = new SettingsWindow();
                nMemory = new MemoryWindow();
                nMeeting = new MeetingWindow();
                nStory = new StorytellerWindow();
                nChat.OpenPersona = h => nPersona.Open(h);
                nChat.OpenMemories = h => nMemory.Open(h);
                nChat.OpenMeeting = h => nMeeting.Open(h);
                nLog.OpenMeeting = () => nMeeting.Open(Game.SelectedWorker() ?? nChat.Target);
                nLog.OpenStoryteller = () => nStory.Visible = !nStory.Visible;
                nStory.OpenPersonality = () => nPersona.OpenText(T.Get("Personalidad del cronista", "The chronicler's personality"),
                    T.Get("Quién es el narrador y cómo habla. Vacío = un cronista medieval irónico.", "Who the narrator is and how it speaks. Empty = a wry medieval chronicler."),
                    Cfg.StorytellerPrompt.Value, v => { Cfg.StorytellerPrompt.Value = v.Trim(); Cfg.Save(); });
                if (!nLog.Ok || !nChat.Ok || !nPersona.Ok || !nSettings.Ok || !nMemory.Ok || !nMeeting.Ok || !nStory.Ok) throw new Exception("template missing");
                // Side by side instead of stacked: live log on the left, chat on the right.
                nLog.Window.Frame.anchoredPosition += new Vector2(-430f, 0f);
                nChat.Window.Frame.anchoredPosition += new Vector2(430f, 0f);
                nMeeting.Window.Frame.anchoredPosition += new Vector2(430f, 0f);
                nStory.Window.Frame.anchoredPosition += new Vector2(-430f, 0f);
                // Hand over whatever the fallback windows had open.
                if (showLog) nLog.Visible = true;
                if (showChat && chatTarget != null) nChat.Open(chatTarget);
                if (showSettings) nSettings.Visible = true;
                showLog = showChat = showSettings = false;
                HookEscape();
                HearthtalkPlugin.Log.LogInfo("native windows ready");
            }
            catch (Exception e)
            {
                nativeFailedFor = Game.VillageKey;
                nLog = null;
                HearthtalkPlugin.Log.LogWarning("native windows unavailable, using fallback: " + e);
            }
        }

        private Func<bool> escActive;
        private Action escAction;
        private int lastEscFrame = -1;

        private void Awake()
        {
            escActive = () => Native && AllWindows().Any(w => w.Visible);
            escAction = CloseTopWindow;
        }

        /// <summary>Joins the game's own Escape stack, the same way its popups close.</summary>
        private void HookEscape()
        {
            if (!NSEipix.Base.MonoSingleton<NSMedieval.Manager.GlobalKeybindingManager>.IsInstantiated()) return;
            NSEipix.Base.MonoSingleton<NSMedieval.Manager.GlobalKeybindingManager>.Instance.SubscribeToEscapeKey(escAction, escActive);
        }

        private void CloseTopWindow()
        {
            if (lastEscFrame == Time.frameCount || !Native) return;
            lastEscFrame = Time.frameCount;
            NativeWindow top = null;
            int best = -1;
            foreach (var w in AllWindows())
            {
                if (!w.Visible) continue;
                int idx = w.Root.transform.GetSiblingIndex();
                if (idx > best) { best = idx; top = w; }
            }
            if (top == null) return;
            top.Visible = false;
            if (top == nSettings.Window) Cfg.Save();
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        }

        private NativeWindow[] AllWindows() => new[] { nPersona.Window, nMemory.Window, nChat.Window, nMeeting.Window, nStory.Window, nSettings.Window, nLog.Window };

        private void Update()
        {
            EnsureNative();
            bool nativeTyping = Native && AllWindows().Any(w => w.Visible && w.InputFocused());
            // While typing the game's input is off, so its Escape stack never runs: close from here.
            if (nativeTyping && Input.GetKeyDown(KeyCode.Escape))
                CloseTopWindow();
            ChatPause.Set(Native && Cfg.PauseWhileChatting.Value && (nChat.Visible || nMeeting.Visible));
            if (Native)
            {
                nLog.Tick(); nChat.Tick(); nPersona.Tick(); nSettings.Tick(); nMemory.Tick(); nMeeting.Tick(); nStory.Tick();
                nativeTyping = AllWindows().Any(w => w.Visible && w.InputFocused());
            }
            bool overWindow = false;
            if (showLog || showChat || showSettings)
            {
                var m = Input.mousePosition;
                var p = new Vector2(m.x, Screen.height - m.y) / scale;
                overWindow = (showLog && logRect.Contains(p)) || (showChat && chatRect.Contains(p)) || (showSettings && settingsRect.Contains(p));
            }
            bool typing = GUIUtility.keyboardControl != 0 && (showChat || showSettings);
            Game.SetGameInputBlocked(overWindow || typing || resizing != 0 || nativeTyping);
        }

        private void OnEnable()
        {
            UIIntegration.TopButtonClicked += ToggleLog;
            UIIntegration.TalkClicked += OpenChat;
            PrivateMessages.OpenChat = OpenChat;
        }

        private void OnDisable()
        {
            UIIntegration.TopButtonClicked -= ToggleLog;
            UIIntegration.TalkClicked -= OpenChat;
            Game.SetGameInputBlocked(false);
            ChatPause.Set(false);
        }

        private void ToggleLog()
        {
            if (Native) nLog.Visible = !nLog.Visible; else showLog = !showLog;
        }

        private void ToggleSettings()
        {
            if (Native) nSettings.Visible = !nSettings.Visible; else showSettings = !showSettings;
        }

        private void OnGUI()
        {
            EnsureStyles();
            scale = Cfg.UiScale.Value > 0 ? Cfg.UiScale.Value : Mathf.Clamp(Screen.height / 1080f, 1f, 2.5f);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            float vw = Screen.width / scale, vh = Screen.height / scale;

            ReleaseTypingOnEscape();
            if (!Game.InVillage) { if (showSettings) DrawWindow(SettingsId, ref settingsRect, DrawSettings, "Hearthtalk — " + T.Get("Ajustes", "Settings"), vw, vh); return; }

            if (Native) return;
            if (showLog) DrawWindow(LogId, ref logRect, DrawLog, T.Get("Conversaciones en vivo", "Live conversations") , vw, vh);
            if (showChat) DrawWindow(ChatId, ref chatRect, DrawChat, T.Get("Hablar con ", "Talk to ") + (chatTarget != null ? chatTarget.GetFullName() : "…"), vw, vh);
            if (showSettings) DrawWindow(SettingsId, ref settingsRect, DrawSettings, "Hearthtalk — " + T.Get("Ajustes", "Settings"), vw, vh);
        }

        // ---------- open / close ----------

        private void ReleaseTypingOnEscape()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && GUIUtility.keyboardControl != 0) { GUIUtility.keyboardControl = 0; e.Use(); }
        }

        private void OpenChat(HumanoidInstance h)
        {
            if (h == null) return;
            if (Native)
            {
                var selected = Game.SelectedWorkers();
                if (selected.Count >= 2 && selected.Contains(h)) nMeeting.Open(h);
                else nChat.Open(h);
                return;
            }
            chatTarget = h;
            showChat = true;
            chatStick = true;
            seenChatCount = -1;
            GUI.FocusControl(InputControl);
        }


        private Vector2 resizeDelta;

        private void DrawWindow(int id, ref Rect rect, GUI.WindowFunction body, string title, float vw, float vh)
        {
            float width = rect.width, height = rect.height;
            resizeDelta = Vector2.zero;
            rect = GUI.Window(id, rect, wid =>
            {
                if (GUI.Button(new Rect(width - 30, 6, 24, 22), "x", buttonStyle))
                {
                    if (wid == LogId) showLog = false; else if (wid == ChatId) showChat = false; else showSettings = false;
                    GUIUtility.keyboardControl = 0;
                }
                GUILayout.Space(6);
                body(wid);
                ResizeHandle(wid, width, height);
                GUI.DragWindow(new Rect(0, 0, 10000, 28));
            }, title, windowStyle);
            rect.width = Mathf.Clamp(rect.width + resizeDelta.x, 320, vw);
            rect.height = Mathf.Clamp(rect.height + resizeDelta.y, 220, vh);
            rect.x = Mathf.Clamp(rect.x, 0, vw - 80);
            rect.y = Mathf.Clamp(rect.y, 0, vh - 40);
        }

        private void ResizeHandle(int id, float width, float height)
        {
            var handle = new Rect(width - 20, height - 20, 20, 20);
            GUI.Label(handle, "//", smallStyle);
            var e = Event.current;
            if (e.type == EventType.MouseDown && handle.Contains(e.mousePosition)) { resizing = id; e.Use(); }
            else if (resizing == id && e.type == EventType.MouseDrag) { resizeDelta += e.delta; e.Use(); }
            else if (resizing == id && e.rawType == EventType.MouseUp) resizing = 0;
        }

        // ---------- live log ----------

        private void DrawLog(int id)
        {
            GUILayout.BeginHorizontal();
            aiOnly = GUILayout.Toggle(aiOnly, " " + T.Get("Solo diálogos IA", "AI dialogue only"), GUILayout.Width(170));
            GUILayout.FlexibleSpace();
            string state = Conversations.LLMReady()
                ? (Cfg.ConversationsEnabled.Value ? T.Get("IA activa", "AI on") : T.Get("IA de conversaciones apagada", "Conversation AI off"))
                : T.Get("Sin proveedor — abre Ajustes", "No provider — open Settings");
            GUILayout.Label(state, smallStyle);
            if (GUILayout.Button(T.Get("Ajustes", "Settings"), buttonStyle, GUILayout.Width(80))) showSettings = !showSettings;
            GUILayout.EndHorizontal();

            var entries = ChatLog.Entries;
            int from = Math.Max(0, entries.Count - 400);
            if (ChatLog.Version != seenLogVersion && logStick) logScroll.y = float.MaxValue;
            seenLogVersion = ChatLog.Version;

            float before = logScroll.y;
            logScroll = GUILayout.BeginScrollView(logScroll);
            string lastTime = null;
            for (int i = from; i < entries.Count; i++)
            {
                var e = entries[i];
                if (aiOnly && e.Kind == LogKind.Native) continue;
                if (e.Time != lastTime && e.Kind != LogKind.Native) { GUILayout.Label($"<color=#8c8273>— {e.Time} —</color>", smallStyle); lastTime = e.Time; }
                switch (e.Kind)
                {
                    case LogKind.Native: GUILayout.Label($"<color=#8f887d><i>[{e.Time}] {e.Text}</i></color>", smallStyle); break;
                    case LogKind.Player: GUILayout.Label($"<color=#e9c46a><b>{e.Speaker}:</b></color> {e.Text}", labelStyle); break;
                    case LogKind.System: GUILayout.Label($"<color=#c0392b>{e.Text}</color>", smallStyle); break;
                    default: GUILayout.Label($"<color={ColorFor(e.Speaker)}><b>{e.Speaker}:</b></color> {e.Text}", labelStyle); break;
                }
            }
            if (entries.Count == 0)
                GUILayout.Label(T.Get("Aún no hay conversaciones. Los colonos hablan solos mientras el juego corre; aquí verás cada diálogo.",
                    "No conversations yet. Colonists talk on their own while the game runs; every dialogue shows up here."), smallStyle);
            GUILayout.EndScrollView();
            if (Event.current.type == EventType.ScrollWheel || (before != logScroll.y && before != float.MaxValue)) logStick = false;
            if (GUILayout.Button(T.Get("Ir al final", "Jump to latest"), buttonStyle, GUILayout.Height(22))) { logStick = true; logScroll.y = float.MaxValue; }
            if (!string.IsNullOrEmpty(Conversations.LastError)) GUILayout.Label("<color=#e07a5f>⚠ " + Conversations.LastError + "</color>", smallStyle);
        }

        // ---------- one-to-one chat ----------

        private void DrawChat(int id)
        {
            if (chatTarget == null || chatTarget.HasDisposed) { chatTarget = Game.Workers().FirstOrDefault(); if (chatTarget == null) { GUILayout.Label("—"); return; } }

            var hist = DirectChat.History(chatTarget);
            if (hist.Count != seenChatCount && chatStick) chatScroll.y = float.MaxValue;
            seenChatCount = hist.Count;

            chatScroll = GUILayout.BeginScrollView(chatScroll);
            if (hist.Count == 0)
                GUILayout.Label(T.Get($"Empieza una conversación con {Game.FirstName(chatTarget)}. Conoce su historia, su ánimo, sus necesidades y lo que ha vivido en la aldea.",
                    $"Start a conversation with {Game.FirstName(chatTarget)}. They know their story, mood, needs and what they have lived through in the village."), smallStyle);
            foreach (var t in hist)
            {
                GUILayout.BeginHorizontal();
                if (t.FromPlayer) GUILayout.FlexibleSpace();
                GUILayout.Label((t.FromPlayer ? "" : $"<b><color={ColorFor(chatTarget.GetFullName())}>{Game.FirstName(chatTarget)}</color></b>\n") + t.Text,
                    t.FromPlayer ? bubblePlayer : bubbleColonist, GUILayout.MaxWidth(chatRect.width * 0.78f));
                if (!t.FromPlayer) GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            if (DirectChat.IsWaiting(chatTarget))
                GUILayout.Label($"<i>{Game.FirstName(chatTarget)} {T.Get("está pensando…", "is thinking…")}</i>", smallStyle);
            GUILayout.EndScrollView();

            var e = Event.current;
            bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == InputControl;
            if (enter) e.Use();

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName(InputControl);
            input = GUILayout.TextField(input ?? "", 600, textFieldStyle, GUILayout.Height(30));
            bool send = GUILayout.Button(T.Get("Enviar", "Send"), buttonStyle, GUILayout.Width(80), GUILayout.Height(30));
            GUILayout.EndHorizontal();
            if ((enter || send) && !string.IsNullOrWhiteSpace(input) && !DirectChat.IsWaiting(chatTarget))
            {
                DirectChat.Send(chatTarget, input);
                input = "";
                chatStick = true;
                GUI.FocusControl(InputControl);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(Conversations.LLMReady() ? "" : "<color=#e07a5f>" + T.Get("Configura un proveedor en Ajustes.", "Set up a provider in Settings.") + "</color>", smallStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(T.Get("Borrar historial", "Clear history"), buttonStyle, GUILayout.Width(130))) DirectChat.Clear(chatTarget);
            GUILayout.EndHorizontal();
        }

        // ---------- settings ----------

        private void DrawSettings(int id)
        {
            settingsScroll = GUILayout.BeginScrollView(settingsScroll);
            Header(T.Get("Proveedor de IA", "AI provider"));
            var names = new[] { "Player2", "Gemini", "OpenRouter", T.Get("Local / OpenAI", "Local / OpenAI") };
            int sel = GUILayout.Toolbar((int)Cfg.Provider.Value, names, buttonStyle, GUILayout.Height(26));
            if (sel != (int)Cfg.Provider.Value) { Cfg.Provider.Value = (LLMProvider)sel; if (Cfg.Provider.Value == LLMProvider.Player2) Player2Auth.TryAutoConnect(); }

            switch (Cfg.Provider.Value)
            {
                case LLMProvider.Player2:
                    GUILayout.Label(T.Get("Gratis con tu cuenta de Player2. Si tienes la app abierta se conecta sola.", "Free with your Player2 account. Connects by itself if the app is open."), smallStyle);
                    GUILayout.Label((Player2Auth.IsConnected ? "<color=#81b29a>● " + T.Get("Conectado", "Connected") : "<color=#e07a5f>● " + T.Get("No conectado", "Not connected")) + "</color>  " + Player2Auth.Status, labelStyle);
                    if (!string.IsNullOrEmpty(Player2Auth.UserCode))
                    {
                        GUILayout.Label(T.Get("Código: ", "Code: ") + "<size=22><b>" + Player2Auth.UserCode + "</b></size>", labelStyle);
                        if (GUILayout.Button(T.Get("Abrir el navegador otra vez", "Open the browser again"), buttonStyle)) Application.OpenURL(Player2Auth.VerificationUrl);
                    }
                    GUILayout.BeginHorizontal();
                    GUI.enabled = !Player2Auth.Busy;
                    if (GUILayout.Button(T.Get("Conectar con la app", "Connect with the app"), buttonStyle)) Player2Auth.ConnectLocal();
                    if (GUILayout.Button(T.Get("Conectar con el navegador", "Connect with the browser"), buttonStyle)) Player2Auth.ConnectBrowser();
                    GUI.enabled = Player2Auth.IsConnected;
                    if (GUILayout.Button(T.Get("Desconectar", "Disconnect"), buttonStyle)) Player2Auth.Disconnect();
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                    break;
                case LLMProvider.Gemini:
                    Field("API key", Cfg.GeminiKey, true);
                    Field(T.Get("Modelo", "Model"), Cfg.GeminiModel, false);
                    if (GUILayout.Button(T.Get("Conseguir una API key gratis", "Get a free API key"), buttonStyle)) Application.OpenURL("https://aistudio.google.com/apikey");
                    break;
                case LLMProvider.OpenRouter:
                    Field("API key", Cfg.OpenRouterKey, true);
                    Field(T.Get("Modelo", "Model"), Cfg.OpenRouterModel, false);
                    break;
                default:
                    GUILayout.Label(T.Get("LM Studio, Ollama, KoboldCpp o cualquier servidor compatible con /v1/chat/completions.", "LM Studio, Ollama, KoboldCpp or any /v1/chat/completions server."), smallStyle);
                    Field("URL", Cfg.CustomEndpoint, false);
                    Field(T.Get("Modelo", "Model"), Cfg.CustomModel, false);
                    Field("API key", Cfg.CustomKey, true);
                    break;
            }

            Header(T.Get("Conversaciones entre colonos", "Colonist conversations"));
            Toggle(Cfg.ConversationsEnabled, T.Get("Convertir sus charlas en diálogos reales con IA", "Turn their chats into real AI dialogue"));
            Toggle(Cfg.ShowBubbles, T.Get("Mostrar burbujas de diálogo sobre los colonos", "Show speech bubbles over colonists"));
            Toggle(Cfg.LogNativeConversations, T.Get("Anotar en el registro las charlas sin IA", "List non-AI chats in the live log"));
            Slider(Cfg.ConversationCooldown, 5f, 180f, T.Get("Pausa mínima entre diálogos IA: {0:0} s", "Minimum gap between AI dialogues: {0:0} s"));
            SliderInt(Cfg.ConversationLines, 2, 8, T.Get("Líneas por diálogo: {0}", "Lines per dialogue: {0}"));
            Slider(Cfg.BubbleSecondsPerLine, 2f, 15f, T.Get("Duración de cada burbuja: {0:0.#} s", "Bubble duration: {0:0.#} s"));

            Header(T.Get("Escritura", "Writing"));
            Field(T.Get("Idioma (auto = el del juego)", "Language (auto = game's)"), Cfg.Language, false);
            Slider(Cfg.Temperature, 0f, 1.5f, T.Get("Creatividad: {0:0.00}", "Creativity: {0:0.00}"));
            GUILayout.Label(T.Get("Instrucciones extra para la IA (tono, estilo, límites):", "Extra instructions for the AI (tone, style, limits):"), smallStyle);
            Cfg.GlobalPrompt.Value = GUILayout.TextArea(Cfg.GlobalPrompt.Value ?? "", 2000, textFieldStyle, GUILayout.MinHeight(60));

            Header(T.Get("Interfaz", "Interface"));
            Slider(Cfg.UiScale, 0f, 3f, T.Get("Escala de ventanas: {0:0.00} (0 = automática)", "Window scale: {0:0.00} (0 = automatic)"));
            Toggle(Cfg.DebugLog, T.Get("Guardar prompts y respuestas en debug.log", "Save prompts and replies to debug.log"));
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(T.Get("Guardar", "Save"), buttonStyle, GUILayout.Height(28))) { Cfg.Save(); GUIUtility.keyboardControl = 0; }
            if (GUILayout.Button(T.Get("Probar conexión", "Test connection"), buttonStyle, GUILayout.Height(28))) TestConnection();
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(testResult)) GUILayout.Label(testResult, smallStyle);
        }

        private string testResult;

        private void TestConnection()
        {
            Cfg.Save();
            testResult = T.Get("Probando…", "Testing…");
            var msgs = new List<Msg> { new Msg("system", $"Answer in {T.StoryLanguage} with one short medieval greeting."), new Msg("user", "Hello!") };
            LLMClient.Complete(msgs, 60, "TEST", (text, err) =>
                testResult = err == null ? "<color=#81b29a>✔ " + text + "</color>" : "<color=#e07a5f>✖ " + err + "</color>");
        }

        private void Header(string text)
        {
            GUILayout.Space(8);
            GUILayout.Label(text, headerStyle);
        }

        private void Field(string label, BepInEx.Configuration.ConfigEntry<string> entry, bool secret)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, labelStyle, GUILayout.Width(170));
            entry.Value = secret ? GUILayout.PasswordField(entry.Value ?? "", '•', textFieldStyle) : GUILayout.TextField(entry.Value ?? "", textFieldStyle);
            GUILayout.EndHorizontal();
        }

        private void Toggle(BepInEx.Configuration.ConfigEntry<bool> entry, string label)
        {
            entry.Value = GUILayout.Toggle(entry.Value, " " + label);
        }

        private void Slider(BepInEx.Configuration.ConfigEntry<float> entry, float min, float max, string fmt)
        {
            GUILayout.Label(string.Format(fmt, entry.Value), smallStyle);
            entry.Value = GUILayout.HorizontalSlider(entry.Value, min, max);
        }

        private void SliderInt(BepInEx.Configuration.ConfigEntry<int> entry, int min, int max, string fmt)
        {
            GUILayout.Label(string.Format(fmt, entry.Value), smallStyle);
            entry.Value = Mathf.RoundToInt(GUILayout.HorizontalSlider(entry.Value, min, max));
        }

        // ---------- looks ----------

        private static readonly string[] palette = { "#e9c46a", "#f4a261", "#81b29a", "#8ecae6", "#e5989b", "#b5838d", "#cdb4db", "#a3b18a", "#ffb703", "#90be6d" };

        private static string ColorFor(string name)
        {
            if (string.IsNullOrEmpty(name)) return "#cccccc";
            int h = 0;
            foreach (char c in name) h = h * 31 + c;
            return palette[Math.Abs(h) % palette.Length];
        }

        private void EnsureStyles()
        {
            if (windowStyle != null && bgTex != null) return;
            bgTex = Tex(new Color(0.09f, 0.075f, 0.06f, 0.95f));
            headerTex = Tex(new Color(0.22f, 0.17f, 0.11f, 1f));
            playerTex = Tex(new Color(0.30f, 0.24f, 0.13f, 0.95f));
            colonistTex = Tex(new Color(0.16f, 0.14f, 0.12f, 0.95f));

            windowStyle = new GUIStyle(GUI.skin.window) { richText = true, fontSize = 15, fontStyle = FontStyle.Bold, padding = new RectOffset(10, 10, 32, 10), alignment = TextAnchor.UpperLeft };
            windowStyle.normal.background = windowStyle.onNormal.background = bgTex;
            windowStyle.normal.textColor = windowStyle.onNormal.textColor = new Color(0.93f, 0.85f, 0.68f);
            labelStyle = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 15 };
            labelStyle.normal.textColor = new Color(0.92f, 0.9f, 0.86f);
            smallStyle = new GUIStyle(labelStyle) { fontSize = 13 };
            smallStyle.normal.textColor = new Color(0.75f, 0.72f, 0.66f);
            nameStyle = new GUIStyle(labelStyle) { fontStyle = FontStyle.Bold };
            headerStyle = new GUIStyle(labelStyle) { fontSize = 15, fontStyle = FontStyle.Bold, padding = new RectOffset(6, 6, 3, 3) };
            headerStyle.normal.background = headerTex;
            headerStyle.normal.textColor = new Color(0.93f, 0.85f, 0.68f);
            bubblePlayer = new GUIStyle(labelStyle) { padding = new RectOffset(10, 10, 6, 6), margin = new RectOffset(4, 4, 4, 4) };
            bubblePlayer.normal.background = playerTex;
            bubbleColonist = new GUIStyle(bubblePlayer);
            bubbleColonist.normal.background = colonistTex;
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 13, richText = true };
            textFieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 15, wordWrap = true };
        }

        private static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }
    }
}
