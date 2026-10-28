using System;
using System.Collections.Concurrent;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Hearthtalk
{
    [BepInPlugin(Guid, "Hearthtalk", Version)]
    public class HearthtalkPlugin : BaseUnityPlugin
    {
        public const string Guid = "hearthtalk";
        public const string Version = "1.0.0";

        internal static HearthtalkPlugin Instance;
        internal static ManualLogSource Log;
        internal static string DataDir;

        private static readonly ConcurrentQueue<Action> mainQueue = new ConcurrentQueue<Action>();
        private static int lastTickFrame = -1;
        private static HearthUI uiHost;
        private static float nextHeartbeat;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            DataDir = Path.Combine(Paths.ConfigPath, "Hearthtalk");
            Directory.CreateDirectory(DataDir);
            Cfg.Bind(Config);
            new Harmony(Guid).PatchAll(typeof(HearthtalkPlugin).Assembly);
            Game.VillageChanged += ChatLog.OnVillageChanged;
            Game.VillageChanged += DirectChat.OnVillageChanged;
            Game.VillageChanged += Bubbles.ClearAll;
            Game.VillageChanged += Personas.OnVillageChanged;
            Game.VillageChanged += PrivateMessages.OnVillageChanged;
            Game.VillageChanged += Memory.OnVillageChanged;
            Game.VillageChanged += GroupChat.OnVillageChanged;
            Game.VillageChanged += Storyteller.OnVillageChanged;

            // Scene objects and the PlayerLoop get replaced when a save loads; these native callbacks keep firing every frame.
            Canvas.willRenderCanvases += SafeTick;
            Application.onBeforeRender += SafeTick;
            Camera.onPreRender += _ => SafeTick();

            if (Cfg.Provider.Value == LLMProvider.Player2)
                Player2Auth.TryAutoConnect();
            Log.LogInfo($"Hearthtalk {Version} loaded");
        }

        internal static void RunOnMain(Action a) => mainQueue.Enqueue(a);

        private static void SafeTick()
        {
            try { Tick(); }
            catch (Exception e) { Log?.LogError("tick failed: " + e); }
        }

        private static void Tick()
        {
            if (Time.frameCount == lastTickFrame) return;
            lastTickFrame = Time.frameCount;

            while (mainQueue.TryDequeue(out var a))
            {
                try { a(); }
                catch (Exception e) { Log.LogError(e); }
            }

            if (uiHost == null)
            {
                var go = new GameObject("HearthtalkUI");
                DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                uiHost = go.AddComponent<HearthUI>();
            }

            Game.Update();
            Bubbles.Update();
            Conversations.Update();
            Actions.EnsureRegistered();
            PrivateMessages.Update();
            Memory.Update();

            if (Cfg.Provider.Value == LLMProvider.Player2 && Time.unscaledTime > nextHeartbeat)
            {
                nextHeartbeat = Time.unscaledTime + 60f;
                Player2Auth.Heartbeat();
            }
        }
    }
}
