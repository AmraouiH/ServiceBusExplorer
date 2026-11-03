using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using ServiceBusExplorer.Localization;

namespace ServiceBusExplorer.Services.Azure
{
    /// <summary>Resources the tool asks a Microsoft Entra token for.</summary>
    public static class TokenResource
    {
        /// <summary>Azure Resource Manager: reads the namespaces and their entities.</summary>
        public const string Management = "https://management.azure.com/";

        /// <summary>Service Bus data plane: peeks the messages.</summary>
        public const string ServiceBus = "https://servicebus.azure.net/";
    }

    /// <summary>Gives Microsoft Entra tokens for a tenant (null = default tenant of the account) and a <see cref="TokenResource"/>.</summary>
    public interface IArmTokenProvider
    {
        string GetToken(string tenantId, string resource, CancellationToken token);
    }

    /// <summary>
    /// Microsoft Entra sign-in in the default browser (authorization code + PKCE, loopback redirect), written with
    /// plain HTTP so the tool keeps a single DLL. Tokens stay in memory only and are shared by the open tool tabs.
    /// </summary>
    public sealed class BrowserTokenProvider : IArmTokenProvider
    {
        /// <summary>Public client of the Azure CLI, already consented in most tenants; can be replaced in the options.</summary>
        public const string DefaultClientId = "04b07795-8ddb-461a-bbee-02f9e1bf7b46";

        private const string Authority = "https://login.microsoftonline.com/";
        private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(3);
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Session> Sessions = new Dictionary<string, Session>(StringComparer.OrdinalIgnoreCase);

        private readonly string _clientId;
        private readonly string _defaultTenant;

        private sealed class Session
        {
            public string AccessToken;
            public string RefreshToken;
            public DateTime ExpiresOn;
        }

        public BrowserTokenProvider(string clientId, string defaultTenant)
        {
            _clientId = string.IsNullOrWhiteSpace(clientId) ? DefaultClientId : clientId.Trim();
            _defaultTenant = string.IsNullOrWhiteSpace(defaultTenant) ? "organizations" : defaultTenant.Trim();
        }

        public string GetToken(string tenantId, string resource, CancellationToken token)
        {
            var tenant = string.IsNullOrWhiteSpace(tenantId) ? _defaultTenant : tenantId;
            var scope = (resource ?? TokenResource.Management) + ".default offline_access";
            var key = tenant + "|" + scope;
            lock (Gate)
            {
                if (Sessions.TryGetValue(key, out var session) && session.ExpiresOn > DateTime.UtcNow.AddMinutes(2)) return session.AccessToken;

                // A refresh token of any tenant or resource of the account can be exchanged without a new prompt.
                foreach (var refreshToken in Sessions.Values.Select(s => s.RefreshToken).Where(r => r != null).Distinct().ToList())
                {
                    try
                    {
                        return Store(key, Redeem(tenant, scope, new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, token));
                    }
                    catch (AzureRequestException)
                    {
                        // Expired, or MFA required for this tenant: sign in again below.
                    }
                }
                return SignIn(tenant, scope, key, token);
            }
        }

        private string SignIn(string tenant, string scope, string key, CancellationToken token)
        {
            var redirect = $"http://localhost:{FreePort()}/";
            var verifier = Base64Url(RandomBytes(32));
            string challenge;
            using (var sha = SHA256.Create()) challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            var state = Base64Url(RandomBytes(16));

            var url = $"{Authority}{Uri.EscapeDataString(tenant)}/oauth2/v2.0/authorize?client_id={Uri.EscapeDataString(_clientId)}&response_type=code"
                      + $"&redirect_uri={Uri.EscapeDataString(redirect)}&response_mode=query&scope={Uri.EscapeDataString(scope)}"
                      + $"&code_challenge={challenge}&code_challenge_method=S256&state={state}&prompt=select_account";

            using (var listener = new HttpListener())
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                listener.Prefixes.Add(redirect);
                listener.Start();
                timeout.CancelAfter(SignInTimeout);
                using (timeout.Token.Register(listener.Abort))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

                    HttpListenerContext context;
                    try
                    {
                        context = listener.GetContext();
                    }
                    catch (Exception error) when (error is HttpListenerException || error is ObjectDisposedException)
                    {
                        token.ThrowIfCancellationRequested();
                        throw new AzureRequestException(0, Strings.SignInTimedOut);
                    }

                    var query = context.Request.QueryString;
                    var page = Encoding.UTF8.GetBytes($"<html><head><meta charset=\"utf-8\"></head><body style=\"font-family:Segoe UI;margin:60px;text-align:center\"><h2>{Strings.AppTitle}</h2><p>{Strings.SignInBrowserDone}</p></body></html>");
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.OutputStream.Write(page, 0, page.Length);
                    context.Response.Close();

                    if (query["state"] != state || string.IsNullOrEmpty(query["code"]))
                    {
                        throw new AzureRequestException(0, Strings.SignInFailed, query["error_description"] ?? query["error"]);
                    }

                    return Store(key, Redeem(tenant, scope, new Dictionary<string, string>
                    {
                        ["grant_type"] = "authorization_code",
                        ["code"] = query["code"],
                        ["redirect_uri"] = redirect,
                        ["code_verifier"] = verifier
                    }, token));
                }
            }
        }

        private JObject Redeem(string tenant, string scope, Dictionary<string, string> form, CancellationToken token)
        {
            form["client_id"] = _clientId;
            form["scope"] = scope;
            var uri = new Uri($"{Authority}{Uri.EscapeDataString(tenant)}/oauth2/v2.0/token");
            return JObject.Parse(AzureHttp.Post(uri, new FormUrlEncodedContent(form), token));
        }

        private static string Store(string key, JObject response)
        {
            var session = new Session
            {
                AccessToken = (string)response["access_token"],
                RefreshToken = (string)response["refresh_token"],
                ExpiresOn = DateTime.UtcNow.AddSeconds((int?)response["expires_in"] ?? 3000)
            };
            Sessions[key] = session;
            return session.AccessToken;
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static byte[] RandomBytes(int count)
        {
            var bytes = new byte[count];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return bytes;
        }

        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Token of the account signed in with « az login ».</summary>
    public sealed class AzureCliTokenProvider : IArmTokenProvider
    {
        private const int TimeoutMs = 30000;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(20);
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, KeyValuePair<string, DateTime>> Cache = new Dictionary<string, KeyValuePair<string, DateTime>>();

        /// <summary>Starting az takes more than a second: the token is kept for the many calls of a load.</summary>
        public string GetToken(string tenantId, string resource, CancellationToken token)
        {
            // Only the known resources: this value ends up on a command line.
            resource = resource == TokenResource.ServiceBus ? TokenResource.ServiceBus : TokenResource.Management;
            var key = (tenantId ?? string.Empty) + "|" + resource;
            lock (Gate)
            {
                if (Cache.TryGetValue(key, out var cached) && cached.Value > DateTime.UtcNow) return cached.Key;
                var value = Request(tenantId, resource, token);
                Cache[key] = new KeyValuePair<string, DateTime>(value, DateTime.UtcNow.Add(CacheDuration));
                return value;
            }
        }

        private static string Request(string tenantId, string resource, CancellationToken token)
        {
            var arguments = $"/c az account get-access-token --resource {resource} --output json";

            // Validated: this value ends up on a command line.
            if (Guid.TryParse(tenantId, out var tenant)) arguments += " --tenant " + tenant.ToString("D");

            var start = new ProcessStartInfo("cmd.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(start))
            {
                var error = process.StandardError.ReadToEndAsync();
                var output = process.StandardOutput.ReadToEndAsync();
                var waited = 0;
                while (!process.WaitForExit(200))
                {
                    waited += 200;
                    if (!token.IsCancellationRequested && waited < TimeoutMs) continue;
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                        // Already finished.
                    }
                    token.ThrowIfCancellationRequested();
                    throw new AzureRequestException(0, Strings.AzureCliFailed, "Timeout");
                }

                if (process.ExitCode != 0)
                {
                    var details = error.Result;
                    throw new AzureRequestException(0, process.ExitCode == 9009 || details.Contains("not recognized") ? Strings.AzureCliMissing : Strings.AzureCliFailed, details);
                }
                return (string)JObject.Parse(output.Result)["accessToken"];
            }
        }
    }
}
