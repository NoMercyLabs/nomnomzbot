// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>Round-trips plaintext unchanged — provider-seam tests exercise the music plumbing,
/// not the envelope-encryption stack, which has its own dedicated tests elsewhere.</summary>
internal sealed class PassthroughProtector : ITokenProtector
{
    public Task<string> ProtectAsync(
        string plaintext,
        TokenProtectionContext context,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(plaintext);

    public Task<string?> TryUnprotectAsync(
        string? sealedEnvelope,
        TokenProtectionContext context,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(sealedEnvelope);
}

/// <summary>Hands every named client the one test handler.</summary>
internal sealed class SingleHandlerClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>
/// Records every request a music provider sends (method + absolute URL, plus the JSON body when
/// present) and answers from registered routes; anything unrouted gets a 404 so a test can prove an
/// endpoint was NOT called with real consequences instead of silence.
/// </summary>
internal sealed class RecordingHttpHandler : HttpMessageHandler
{
    private readonly List<(
        Func<HttpRequestMessage, bool> Matches,
        HttpStatusCode Status,
        string? Json
    )> _routes = [];

    public List<string> RequestUrls { get; } = [];

    /// <summary>Body per recorded request, index-aligned with <see cref="RequestUrls"/> ("" when none).</summary>
    public List<string> RequestBodies { get; } = [];

    public void RespondWhen(
        Func<HttpRequestMessage, bool> matches,
        HttpStatusCode status,
        string? json = null
    ) => _routes.Add((matches, status, json));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        RequestUrls.Add($"{request.Method} {request.RequestUri}");
        RequestBodies.Add(
            request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken)
        );

        foreach (
            (Func<HttpRequestMessage, bool> matches, HttpStatusCode status, string? json) in _routes
        )
        {
            if (!matches(request))
                continue;

            HttpResponseMessage response = new(status);
            if (json is not null)
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return response;
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

/// <summary>
/// Builds a real <see cref="YouTubeMusicProvider"/> over the shared test HTTP handler and an in-memory
/// <c>YouTube:ApiKey</c> — mirrors the runtime DI shape (named HttpClient + IConfiguration). A null
/// <paramref name="apiKey"/> leaves the provider unconfigured (search/resolve degrade to empty/null).
/// </summary>
internal static class YouTubeProviderFactory
{
    public static YouTubeMusicProvider Create(
        string? apiKey = null,
        HttpMessageHandler? handler = null
    )
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["YouTube:ApiKey"] = apiKey })
            .Build();

        SingleHandlerClientFactory factory = new(handler ?? new RecordingHttpHandler());

        return new YouTubeMusicProvider(
            factory,
            configuration,
            NullLogger<YouTubeMusicProvider>.Instance
        );
    }
}
