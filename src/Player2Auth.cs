using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hearthtalk
{
    /// <summary>
    /// Player2 sign-in: first silently through the local Player2 app (127.0.0.1:4315),
    /// otherwise with the device-code flow in the browser. The key is kept in the BepInEx config.
    /// </summary>
    internal static class Player2Auth
    {
        private const string LocalApi = "http://127.0.0.1:4315/v1";

        internal static bool IsConnected => !string.IsNullOrEmpty(Cfg.Player2Key.Value);
        internal static bool Busy { get; private set; }
        internal static string Status { get; private set; } = "";
        internal static string UserCode { get; private set; } = "";
        internal static string VerificationUrl { get; private set; } = "";

        internal static void TryAutoConnect()
        {
            if (IsConnected) { Status = T.Get("Sesión guardada", "Saved session"); return; }
            ConnectLocal(silent: true);
        }

        internal static void ConnectLocal(bool silent = false)
        {
            if (Busy) return;
            Busy = true;
            Status = T.Get("Buscando la app de Player2…", "Looking for the Player2 app…");
            Task.Run(async () =>
            {
                string key = null, err = null;
                try
                {
                    var health = await LLMClient.Http.GetAsync(LocalApi + "/health");
                    if (!health.IsSuccessStatusCode) throw new Exception("app");
                    var req = new HttpRequestMessage(HttpMethod.Post, $"{LocalApi}/login/web/{Cfg.Player2ClientId.Value}") { Content = new StringContent("", Encoding.UTF8, "application/json") };
                    req.Headers.TryAddWithoutValidation("player2-game-key", LLMClient.GameKeyHeader);
                    var res = await LLMClient.Http.SendAsync(req);
                    string raw = await res.Content.ReadAsStringAsync();
                    if (!res.IsSuccessStatusCode) throw new Exception($"login HTTP {(int)res.StatusCode}");
                    key = (string)JObject.Parse(raw)["p2Key"];
                }
                catch (Exception e) { err = e.Message; }
                HearthtalkPlugin.RunOnMain(() =>
                {
                    Busy = false;
                    if (!string.IsNullOrEmpty(key))
                    {
                        Store(key);
                        Status = T.Get("Conectado con la app de Player2", "Connected through the Player2 app");
                    }
                    else
                        Status = silent
                            ? T.Get("No conectado. Abre la app de Player2 o conéctate con el navegador.", "Not connected. Open the Player2 app or connect with the browser.")
                            : T.Get("No se encontró la app de Player2 (", "Player2 app not found (") + err + ")";
                });
            });
        }

        internal static void ConnectBrowser()
        {
            if (Busy) return;
            Busy = true;
            Status = T.Get("Pidiendo código…", "Requesting code…");
            Task.Run(async () =>
            {
                try
                {
                    var body = new JObject { ["client_id"] = Cfg.Player2ClientId.Value };
                    var res = await LLMClient.Http.PostAsync(LLMClient.Player2Api + "/login/device/new", new StringContent(body.ToString(), Encoding.UTF8, "application/json"));
                    string raw = await res.Content.ReadAsStringAsync();
                    if (!res.IsSuccessStatusCode) throw new Exception($"HTTP {(int)res.StatusCode}");
                    var j = JObject.Parse(raw);
                    string deviceCode = (string)j["deviceCode"];
                    string userCode = (string)j["userCode"];
                    string uri = (string)j["verificationUri"] ?? "https://player2.game";
                    string uriFull = (string)j["verificationUriComplete"] ?? uri;
                    float interval = j["interval"]?.Value<float>() ?? 5f;
                    HearthtalkPlugin.RunOnMain(() =>
                    {
                        UserCode = userCode; VerificationUrl = uri;
                        Status = T.Get("Introduce el código en el navegador: ", "Enter the code in the browser: ") + userCode;
                        Application.OpenURL(uriFull);
                    });

                    var poll = new JObject { ["client_id"] = Cfg.Player2ClientId.Value, ["device_code"] = deviceCode, ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code" };
                    var deadline = DateTime.UtcNow.AddMinutes(5);
                    while (DateTime.UtcNow < deadline)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(interval));
                        var pr = await LLMClient.Http.PostAsync(LLMClient.Player2Api + "/login/device/token", new StringContent(poll.ToString(), Encoding.UTF8, "application/json"));
                        if (!pr.IsSuccessStatusCode) continue; // 400 = still waiting
                        string key = (string)JObject.Parse(await pr.Content.ReadAsStringAsync())["p2Key"];
                        if (string.IsNullOrEmpty(key)) continue;
                        HearthtalkPlugin.RunOnMain(() =>
                        {
                            Store(key); Busy = false; UserCode = "";
                            Status = T.Get("Conectado con el navegador", "Connected through the browser");
                        });
                        return;
                    }
                    HearthtalkPlugin.RunOnMain(() => { Busy = false; UserCode = ""; Status = T.Get("Se agotó el tiempo. Inténtalo otra vez.", "Timed out. Try again."); });
                }
                catch (Exception e)
                {
                    HearthtalkPlugin.RunOnMain(() => { Busy = false; UserCode = ""; Status = "Error: " + e.Message; });
                }
            });
        }

        internal static void Disconnect()
        {
            Cfg.Player2Key.Value = "";
            Cfg.Save();
            Status = T.Get("Desconectado", "Disconnected");
        }

        internal static void OnUnauthorized()
        {
            Disconnect();
            Status = T.Get("La sesión de Player2 caducó. Vuelve a conectar.", "Player2 session expired. Connect again.");
        }

        /// <summary>Player2 expects a periodic health ping from games using it.</summary>
        internal static void Heartbeat()
        {
            if (!IsConnected) return;
            string key = Cfg.Player2Key.Value;
            Task.Run(async () =>
            {
                try
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, LLMClient.Player2Api + "/health");
                    req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
                    req.Headers.TryAddWithoutValidation("player2-game-key", LLMClient.GameKeyHeader);
                    await LLMClient.Http.SendAsync(req);
                }
                catch { }
            });
        }

        private static void Store(string key)
        {
            Cfg.Player2Key.Value = key;
            Cfg.Save();
        }
    }
}
