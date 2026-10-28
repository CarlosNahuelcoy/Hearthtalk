using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Hearthtalk
{
    internal struct Msg
    {
        public string Role; // system | user | assistant
        public string Content;
        public Msg(string role, string content) { Role = role; Content = content; }
    }

    internal static class LLMClient
    {
        internal const string Player2Api = "https://api.player2.game/v1";
        internal const string GameKeyHeader = "GoingMedieval-Hearthtalk";

        internal static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        private static readonly object debugLock = new object();

        static LLMClient()
        {
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;
        }

        /// <summary>Sends a chat request on a worker thread; onDone(text, error) runs on the game's main thread.</summary>
        internal static void Complete(List<Msg> messages, int maxTokens, string tag, Action<string, string> onDone)
        {
            var provider = Cfg.Provider.Value;
            Task.Run(async () =>
            {
                string text = null, error = null;
                try
                {
                    Debug(tag, "REQUEST", string.Join("\n---\n", messages.Select(m => $"[{m.Role}]\n{m.Content}")));
                    text = provider == LLMProvider.Gemini
                        ? await SendGemini(messages, maxTokens)
                        : await SendOpenAI(provider, messages, maxTokens);
                    text = Clean(text);
                    Debug(tag, "REPLY", text);
                    if (string.IsNullOrWhiteSpace(text)) error = "empty reply";
                }
                catch (Exception e)
                {
                    error = e is TaskCanceledException ? "timeout" : e.Message;
                    Debug(tag, "ERROR", e.ToString());
                }
                HearthtalkPlugin.RunOnMain(() => onDone(text, error));
            });
        }

        private static async Task<string> SendOpenAI(LLMProvider provider, List<Msg> messages, int maxTokens)
        {
            string url, key, model;
            switch (provider)
            {
                case LLMProvider.Player2:
                    if (!Player2Auth.IsConnected) throw new Exception(T.Get("Player2 no está conectado (Ajustes → Conectar).", "Player2 is not connected (Settings → Connect)."));
                    url = Player2Api + "/chat/completions"; key = Cfg.Player2Key.Value; model = null; break;
                case LLMProvider.OpenRouter:
                    url = "https://openrouter.ai/api/v1/chat/completions"; key = Cfg.OpenRouterKey.Value; model = Cfg.OpenRouterModel.Value; break;
                default:
                    url = Cfg.CustomEndpoint.Value; key = Cfg.CustomKey.Value; model = Cfg.CustomModel.Value; break;
            }

            var body = new JObject
            {
                ["messages"] = new JArray(messages.Select(m => new JObject { ["role"] = m.Role, ["content"] = m.Content })),
                ["temperature"] = Cfg.Temperature.Value,
                ["stream"] = false
            };
            // Player2's models may spend the budget on hidden reasoning and cut the reply mid-sentence, so no cap there
            // (EchoColony does the same); elsewhere leave generous room for reasoning models.
            if (provider != LLMProvider.Player2) body["max_tokens"] = maxTokens * 4;
            if (!string.IsNullOrWhiteSpace(model)) body["model"] = model;

            for (int attempt = 0; ; attempt++)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json") };
                if (!string.IsNullOrWhiteSpace(key)) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
                if (provider == LLMProvider.Player2) req.Headers.TryAddWithoutValidation("player2-game-key", GameKeyHeader);
                if (provider == LLMProvider.OpenRouter)
                {
                    req.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/hearthtalk");
                    req.Headers.TryAddWithoutValidation("X-Title", "Hearthtalk");
                }

                var res = await Http.SendAsync(req);
                string raw = await res.Content.ReadAsStringAsync();
                if (res.IsSuccessStatusCode)
                {
                    var j = JObject.Parse(raw);
                    if ((string)j.SelectToken("choices[0].finish_reason") == "length")
                        HearthtalkPlugin.Log.LogWarning("reply was cut by the token limit");
                    return (string)j.SelectToken("choices[0].message.content") ?? (string)j.SelectToken("choices[0].text");
                }
                int code = (int)res.StatusCode;
                if ((code == 429 || code >= 500) && attempt < 2) { await Task.Delay(1500 * (attempt + 1)); continue; }
                if (code == 401 && provider == LLMProvider.Player2) HearthtalkPlugin.RunOnMain(Player2Auth.OnUnauthorized);
                throw new Exception($"HTTP {code}: {Short(raw)}");
            }
        }

        private static async Task<string> SendGemini(List<Msg> messages, int maxTokens)
        {
            string model = Cfg.GeminiModel.Value.Trim();
            if (string.IsNullOrWhiteSpace(Cfg.GeminiKey.Value)) throw new Exception(T.Get("Falta la API key de Gemini (Ajustes).", "Missing Gemini API key (Settings)."));
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

            var system = string.Join("\n\n", messages.Where(m => m.Role == "system").Select(m => m.Content));
            var contents = new JArray();
            foreach (var m in messages.Where(m => m.Role != "system"))
                contents.Add(new JObject { ["role"] = m.Role == "assistant" ? "model" : "user", ["parts"] = new JArray(new JObject { ["text"] = m.Content }) });
            var gen = new JObject { ["maxOutputTokens"] = maxTokens * 4, ["temperature"] = Cfg.Temperature.Value };
            // Flash models spend output tokens on thinking unless it is turned off.
            if (model.Contains("flash")) gen["thinkingConfig"] = new JObject { ["thinkingBudget"] = 0 };
            var body = new JObject { ["contents"] = contents, ["generationConfig"] = gen };
            if (system.Length > 0) body["systemInstruction"] = new JObject { ["parts"] = new JArray(new JObject { ["text"] = system }) };

            for (int attempt = 0; ; attempt++)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json") };
                req.Headers.TryAddWithoutValidation("x-goog-api-key", Cfg.GeminiKey.Value.Trim());
                var res = await Http.SendAsync(req);
                string raw = await res.Content.ReadAsStringAsync();
                if (res.IsSuccessStatusCode)
                {
                    var parts = JObject.Parse(raw).SelectToken("candidates[0].content.parts") as JArray;
                    return parts == null ? null : string.Concat(parts.Where(p => p["thought"]?.Value<bool>() != true).Select(p => (string)p["text"]));
                }
                int code = (int)res.StatusCode;
                if ((code == 429 || code >= 500) && attempt < 2) { await Task.Delay(2000 * (attempt + 1)); continue; }
                throw new Exception($"HTTP {code}: {Short(raw)}");
            }
        }

        private static string Clean(string s)
        {
            if (s == null) return null;
            s = Regex.Replace(s, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase);
            return s.Trim();
        }

        private static string Short(string s) => s == null ? "" : (s.Length > 300 ? s.Substring(0, 300) + "…" : s);

        private static void Debug(string tag, string kind, string text)
        {
            if (!Cfg.DebugLog.Value) return;
            lock (debugLock)
                File.AppendAllText(Path.Combine(HearthtalkPlugin.DataDir, "debug.log"), $"===== {DateTime.Now:HH:mm:ss} {tag} {kind}\n{text}\n\n");
        }
    }
}
