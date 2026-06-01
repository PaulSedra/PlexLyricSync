using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PlexLyricSync.Tests.TestHelpers;

internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(handler(request));
}