using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using Newtonsoft.Json.Linq;
using ServiceBusExplorer.Localization;

namespace ServiceBusExplorer.Services.Azure
{
    /// <summary>HTTP calls to Azure, made from the background tasks.</summary>
    internal static class AzureHttp
    {
        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            return new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        }

        /// <summary>GET returning the body; refused or failed calls raise a readable <see cref="AzureRequestException"/>.</summary>
        public static string Get(Uri uri, string authorization, bool bearer, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
            {
                if (bearer) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authorization);
                else request.Headers.TryAddWithoutValidation("Authorization", authorization);
                return Send(request, token);
            }
        }

        public static string Post(Uri uri, FormUrlEncodedContent content, CancellationToken token)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content })
            {
                return Send(request, token);
            }
        }

        private static string Send(HttpRequestMessage request, CancellationToken token)
        {
            HttpResponseMessage response;
            try
            {
                response = Client.SendAsync(request, token).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (HttpRequestException error)
            {
                throw new AzureRequestException(0, Strings.AzureUnreachable(request.RequestUri.Host), error.GetBaseException().Message);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // Not cancelled by the user: the HTTP timeout expired.
                throw new AzureRequestException(0, Strings.AzureUnreachable(request.RequestUri.Host), "Timeout");
            }

            using (response)
            {
                var body = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                if (response.IsSuccessStatusCode) return body;
                throw new AzureRequestException((int)response.StatusCode, Describe((int)response.StatusCode, request.RequestUri), Clean(body));
            }
        }

        private static string Describe(int status, Uri uri)
        {
            switch (status)
            {
                case 401:
                case 403:
                    return Strings.AzureAccessDenied(uri.Host);
                case 404:
                    return Strings.AzureNotFound;
                case 429:
                    return Strings.AzureThrottled;
                default:
                    return Strings.AzureFailed(status);
            }
        }

        /// <summary>Message of a JSON error, else the raw body.</summary>
        private static string Clean(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                var json = JObject.Parse(body);
                return (string)json.SelectToken("error.message") ?? (string)json["error_description"] ?? body;
            }
            catch (Exception)
            {
                return body;
            }
        }
    }
}
