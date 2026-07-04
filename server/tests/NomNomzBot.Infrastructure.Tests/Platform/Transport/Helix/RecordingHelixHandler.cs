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

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

/// <summary>
/// An in-process <see cref="HttpMessageHandler"/> at the wire seam (twitch-helix.md §10.2). It records every
/// outgoing request (so a test can assert the Client-Id / Authorization actually sent) and replays a scripted
/// queue of responses (so a test can drive 401→refresh, 429→reset, 5xx→retry, and envelope parsing) — fully
/// deterministic, no network.
/// </summary>
public sealed class RecordingHelixHandler(IEnumerable<Func<HttpResponseMessage>> responses)
    : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new(responses);

    public List<RecordedRequest> Requests { get; } = [];

    public int CallCount => Requests.Count;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        string? body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(
            new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.Parameter,
                request.Headers.TryGetValues("Client-Id", out IEnumerable<string>? clientIds)
                    ? clientIds.FirstOrDefault()
                    : null,
                body
            )
        );

        Func<HttpResponseMessage> next =
            _responses.Count > 0
                ? _responses.Dequeue()
                : () => new HttpResponseMessage(HttpStatusCode.OK);

        return next();
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }

    public static HttpResponseMessage Text(HttpStatusCode status, string body, string mediaType)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType),
        };
    }

    public static HttpResponseMessage WithRateLimitHeaders(
        HttpResponseMessage response,
        int limit,
        int remaining,
        DateTimeOffset resetsAt
    )
    {
        response.Headers.Add("Ratelimit-Limit", limit.ToString());
        response.Headers.Add("Ratelimit-Remaining", remaining.ToString());
        response.Headers.Add("Ratelimit-Reset", resetsAt.ToUnixTimeSeconds().ToString());
        return response;
    }
}

/// <summary>One captured outbound request: verb, URL, the Authorization / Client-Id headers it carried, and the serialized body (null when bodyless).</summary>
public sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    string? AuthorizationParameter,
    string? ClientId,
    string? Body = null
);
