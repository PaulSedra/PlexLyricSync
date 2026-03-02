using System.Collections.Generic;
using System.Net.Http;

namespace PlexLyricSync.Core.Clients;
public static class Http
{
    public static readonly HttpClient Client = new();

    /// <summary>
    /// Returns an HttpRequestMessage
    /// </summary>
    /// <param name="requestMethod">method of the request</param>
    /// <param name="requestUrl">url of the request</param>
    /// <param name="headers">optional request headers</param>
    /// <returns>HttpRequestMessage</returns>
    public static HttpRequestMessage HttpRequestMessage(HttpMethod requestMethod, string requestUrl, IReadOnlyDictionary<string, string>? headers = null)
    {
        HttpRequestMessage httpRequestMessage = new(requestMethod, requestUrl);

        if (headers == null) return httpRequestMessage;
        foreach ((string key, string value) in headers)
        {
            httpRequestMessage.Headers.TryAddWithoutValidation(key, value);
        }

        return httpRequestMessage;
    }
}