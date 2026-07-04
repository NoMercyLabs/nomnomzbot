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
using NomNomzBot.Application.Common.Interfaces.Crypto;

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
/// Records every request the Spotify provider sends (method + absolute URL) and answers from
/// registered routes; anything unrouted gets a 404 so a test can prove an endpoint was NOT called
/// with real consequences instead of silence.
/// </summary>
internal sealed class RecordingSpotifyHandler : HttpMessageHandler
{
    private readonly List<(
        Func<HttpRequestMessage, bool> Matches,
        HttpStatusCode Status,
        string? Json
    )> _routes = [];

    public List<string> RequestUrls { get; } = [];

    public void RespondWhen(
        Func<HttpRequestMessage, bool> matches,
        HttpStatusCode status,
        string? json = null
    ) => _routes.Add((matches, status, json));

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        RequestUrls.Add($"{request.Method} {request.RequestUri}");

        foreach (
            (Func<HttpRequestMessage, bool> matches, HttpStatusCode status, string? json) in _routes
        )
        {
            if (!matches(request))
                continue;

            HttpResponseMessage response = new(status);
            if (json is not null)
                response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return Task.FromResult(response);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
