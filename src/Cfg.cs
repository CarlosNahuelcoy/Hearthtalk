using BepInEx.Configuration;
using UnityEngine;

namespace Hearthtalk
{
    public enum LLMProvider { Player2, Gemini, OpenRouter, OpenAICompatible }

    internal static class Cfg
    {
        internal static ConfigFile File;

        internal static ConfigEntry<LLMProvider> Provider;
        internal static ConfigEntry<string> Player2Key;
        internal static ConfigEntry<string> Player2ClientId;
        internal static ConfigEntry<string> GeminiKey;
        internal static ConfigEntry<string> GeminiModel;
        internal static ConfigEntry<string> OpenRouterKey;
        internal static ConfigEntry<string> OpenRouterModel;
        internal static ConfigEntry<string> CustomEndpoint;
        internal static ConfigEntry<string> CustomKey;
        internal static ConfigEntry<string> CustomModel;

        internal static ConfigEntry<string> Language;
        internal static ConfigEntry<string> InterfaceLanguage;
        internal static ConfigEntry<string> GlobalPrompt;
        internal static ConfigEntry<float> Temperature;

        internal static ConfigEntry<bool> ConversationsEnabled;
        internal static ConfigEntry<float> ConversationCooldown;
        internal static ConfigEntry<int> ConversationLines;
        internal static ConfigEntry<bool> ShowBubbles;
        internal static ConfigEntry<float> BubbleSecondsPerLine;
        internal static ConfigEntry<bool> LogNativeConversations;

        internal static ConfigEntry<bool> PrivateMessages;
        internal static ConfigEntry<bool> PrivateOnEvents;
        internal static ConfigEntry<bool> PrivateOnLifeEvents;
        internal static ConfigEntry<bool> PrivateOnNeeds;
        internal static ConfigEntry<float> PrivateRandomChance;
        internal static ConfigEntry<float> PrivateMinMinutes;
        internal static ConfigEntry<float> PrivatePerColonistHours;

        internal static ConfigEntry<bool> AllowMood;
        internal static ConfigEntry<float> MoodCooldownHours;
        internal static ConfigEntry<bool> AllowOrders;

        internal static ConfigEntry<bool> MemoryEnabled;
        internal static ConfigEntry<int> MemoryDaysInPrompt;

        internal static ConfigEntry<bool> Monologues;
        internal static ConfigEntry<float> MonologueChance;

        internal static ConfigEntry<bool> StorytellerEnabled;
        internal static ConfigEntry<bool> StorytellerComments;
        internal static ConfigEntry<bool> StorytellerCanTriggerEvents;
        internal static ConfigEntry<string> StorytellerPrompt;


        internal static ConfigEntry<bool> PauseWhileChatting;
        internal static ConfigEntry<float> UiScale;
        internal static ConfigEntry<bool> DebugLog;

        internal static void Bind(ConfigFile f)
        {
            File = f;
            Provider = f.Bind("1. Provider", "Provider", LLMProvider.Player2, "Which AI service to use: Player2, Gemini, OpenRouter or OpenAICompatible (LM Studio, Ollama, KoboldCpp, any /v1/chat/completions endpoint).");
            Player2Key = f.Bind("1. Provider", "Player2Key", "", "Filled automatically when you connect to Player2. Do not share it.");
            Player2ClientId = f.Bind("1. Provider", "Player2ClientId", "01a182f6-5b70-7c17-b220-03016b09417a", "Player2 game client id used to sign in.");
            GeminiKey = f.Bind("1. Provider", "GeminiApiKey", "", "Google AI Studio API key (https://aistudio.google.com/apikey).");
            GeminiModel = f.Bind("1. Provider", "GeminiModel", "gemini-2.5-flash", "Gemini model id.");
            OpenRouterKey = f.Bind("1. Provider", "OpenRouterApiKey", "", "OpenRouter API key.");
            OpenRouterModel = f.Bind("1. Provider", "OpenRouterModel", "google/gemini-2.5-flash", "OpenRouter model id.");
            CustomEndpoint = f.Bind("1. Provider", "CustomEndpoint", "http://localhost:1234/v1/chat/completions", "OpenAI-compatible chat completions URL (LM Studio default shown; Ollama: http://localhost:11434/v1/chat/completions).");
            CustomKey = f.Bind("1. Provider", "CustomApiKey", "", "Optional bearer key for the custom endpoint.");
            CustomModel = f.Bind("1. Provider", "CustomModel", "", "Model name for the custom endpoint (leave empty if the server does not need one).");

            Language = f.Bind("2. Writing", "Language", "auto", "Language the colonists speak. 'auto' follows the game's language.");
            InterfaceLanguage = f.Bind("2. Writing", "InterfaceLanguage", "English", "Language of Hearthtalk's windows: English or Spanish. Takes full effect after restarting the game.");
            GlobalPrompt = f.Bind("2. Writing", "PlayerInstructions", "", "Extra instructions added to every prompt (tone, style, content limits...).");
            Temperature = f.Bind("2. Writing", "Temperature", 0.9f, "Creativity of the replies (0 to 1.5).");

            ConversationsEnabled = f.Bind("3. Conversations", "Enabled", true, "Turn the game's own colonist conversations into real AI dialogue.");
            ConversationCooldown = f.Bind("3. Conversations", "CooldownSeconds", 20f, "Minimum real seconds between two AI conversations (limits cost). Conversations that fall inside the cooldown keep the game's normal text.");
            ConversationLines = f.Bind("3. Conversations", "LinesPerConversation", 4, "Dialogue lines per conversation (2-8).");
            ShowBubbles = f.Bind("3. Conversations", "ShowBubbles", true, "Show dialogue as speech bubbles over the colonists.");
            BubbleSecondsPerLine = f.Bind("3. Conversations", "BubbleSeconds", 6f, "How long each bubble stays on screen (real seconds).");
            LogNativeConversations = f.Bind("3. Conversations", "LogNativeConversations", true, "Also list the game's normal conversation lines in the live log when no AI dialogue was generated.");

            PrivateMessages = f.Bind("4. Private messages", "Enabled", true, "Colonists come to you on their own with a private message (shown in the game's notice list on the right).");
            PrivateOnEvents = f.Bind("4. Private messages", "OnGameEvents", true, "React to raids, traders, visitors, weather and other game events.");
            PrivateOnLifeEvents = f.Bind("4. Private messages", "OnLifeEvents", true, "React to what happens to them: injuries, fights, illness.");
            PrivateOnNeeds = f.Bind("4. Private messages", "OnNeeds", true, "Speak up when miserable, starving, exhausted or badly hurt.");
            PrivateRandomChance = f.Bind("4. Private messages", "RandomChancePerHour", 0.04f, "Chance per in-game hour that someone comes to you for no special reason (0 = never).");
            PrivateMinMinutes = f.Bind("4. Private messages", "MinRealMinutesBetween", 4f, "Minimum real minutes between two private messages.");
            PrivatePerColonistHours = f.Bind("4. Private messages", "PerColonistCooldownHours", 24f, "In-game hours before the same colonist writes to you again.");

            AllowMood = f.Bind("5. Influence", "ConversationsAffectMood", true, "What you say in chat can raise or lower the colonist's mood (a visible thought, limited in size and frequency).");
            MoodCooldownHours = f.Bind("5. Influence", "MoodCooldownHours", 12f, "In-game hours before a chat can change the same colonist's mood again.");
            AllowOrders = f.Bind("5. Influence", "ColonistsFollowOrders", true, "Colonists can accept requests to change their job priorities and daily schedule. They may refuse.");

            MemoryEnabled = f.Bind("6. Memory", "Enabled", true, "At the end of each day colonists who talked write a short memory of it, used in later conversations.");
            MemoryDaysInPrompt = f.Bind("6. Memory", "DaysInPrompt", 7, "How many past days a colonist remembers in prompts.");

            Monologues = f.Bind("3. Conversations", "Monologues", true, "Colonists sometimes think aloud in a bubble when something happens to them.");
            MonologueChance = f.Bind("3. Conversations", "MonologueChance", 0.35f, "Chance that a notable life event makes the colonist think aloud.");

            StorytellerEnabled = f.Bind("7. Storyteller", "Enabled", true, "A chat with the village's fate: a narrator who comments on events and can answer you.");
            StorytellerComments = f.Bind("7. Storyteller", "CommentOnEvents", true, "The narrator writes a short comment in the log when a game event starts.");
            StorytellerCanTriggerEvents = f.Bind("7. Storyteller", "CanTriggerEvents", false, "Let the narrator start game events (traders, weather, visitors, raids) when you ask for them.");
            StorytellerPrompt = f.Bind("7. Storyteller", "Personality", "", "Who the narrator is and how it speaks. Empty = a wry medieval chronicler.");


            PauseWhileChatting = f.Bind("4. Interface", "PauseWhileChatting", false, "Pause the game while a chat or meeting window is open.");
            UiScale = f.Bind("4. Interface", "UiScale", 0f, "Window scale. 0 = automatic from screen height.");
            DebugLog = f.Bind("5. Debug", "LogPrompts", false, "Write every prompt and reply to BepInEx/config/Hearthtalk/debug.log.");
        }

        internal static void Save() => File.Save();
    }
}
