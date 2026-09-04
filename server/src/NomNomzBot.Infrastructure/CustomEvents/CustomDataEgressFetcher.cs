// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Infrastructure.Sandbox;

namespace NomNomzBot.Infrastructure.CustomEvents;

/// <inheritdoc cref="ICustomDataEgressFetcher"/>
internal sealed class CustomDataEgressFetcher : ICustomDataEgressFetcher
{
    /// <summary>Response-body read cap (custom-events.md D4) — shared by the poll ingress and the test-fetch preview.</summary>
    public const int MaxResponseBytes = 64 * 1024;

    private readonly IApplicationDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CustomDataEgressFetcher> _logger;

    public CustomDataEgressFetcher(
        IApplicationDbContext db,
        IHttpClientFactory httpClientFactory,
        ILogger<CustomDataEgressFetcher> logger
    )
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<CustomDataEgressFetchResult> FetchAsync(
        Guid broadcasterId,
        string? endpointUrl,
        string? bearerToken,
        CancellationToken ct = default
    )
    {
        if (
            string.IsNullOrWhiteSpace(endpointUrl)
            || !Uri.TryCreate(endpointUrl, UriKind.Absolute, out Uri? endpoint)
        )
            return CustomDataEgressFetchResult.Fail(
                CustomDataEgressFetchOutcome.NoUrl,
                "No usable absolute endpoint URL is configured."
            );

        // ── SSRF gate ──────────────────────────────────────────────────────────────────────────
        // The endpoint host is user-supplied, so it MUST match an enabled egress-allowlist row for this channel
        // before any fetch. This mirrors the outbound-webhook Fqdn match (the single H.7 boundary); the
        // EgressHttpClient adds resolve-then-pin + non-public-IP rejection as defense in depth.
        string host = endpoint.Host;
        bool allowed = await _db.HttpEgressAllowlists.AnyAsync(
            a =>
                a.BroadcasterId == broadcasterId
                && a.Fqdn == host
                && a.IsEnabled
                && a.DeletedAt == null,
            ct
        );
        if (!allowed)
        {
            _logger.LogWarning(
                "Custom data egress fetch on channel {Channel} targets non-allowlisted host '{Host}' — skipped (SSRF egress gate).",
                broadcasterId,
                host
            );
            return CustomDataEgressFetchResult.Fail(
                CustomDataEgressFetchOutcome.NotAllowlisted,
                $"The target host '{host}' is not in an enabled egress allowlist."
            );
        }

        using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
        if (!string.IsNullOrWhiteSpace(bearerToken))
            request.Headers.Authorization = new("Bearer", bearerToken);

        HttpClient client = _httpClientFactory.CreateClient(EgressHttpClient.Name);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return CustomDataEgressFetchResult.Fail(
                CustomDataEgressFetchOutcome.HttpError,
                ex.Message
            );
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return CustomDataEgressFetchResult.Fail(
                    CustomDataEgressFetchOutcome.HttpError,
                    $"HTTP {(int)response.StatusCode} from the endpoint."
                );

            (bool oversize, string body) = await ReadBoundedAsync(response, ct);
            if (oversize)
                return CustomDataEgressFetchResult.Fail(
                    CustomDataEgressFetchOutcome.Oversize,
                    $"Response body exceeded the {MaxResponseBytes} byte cap."
                );

            return CustomDataEgressFetchResult.Ok(body);
        }
    }

    /// <summary>
    /// Reads the response body into a fixed cap. Returns <c>oversize=true</c> (and no body) when the payload
    /// exceeds the cap, so a truncated fragment is never treated as a whole payload.
    /// </summary>
    private static async Task<(bool Oversize, string Body)> ReadBoundedAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        if (response.Content.Headers.ContentLength is { } and > MaxResponseBytes)
            return (true, string.Empty);

        await using System.IO.Stream stream = await response.Content.ReadAsStreamAsync(ct);
        byte[] buffer = new byte[MaxResponseBytes];
        int total = 0;
        int read;
        while (
            total < MaxResponseBytes
            && (read = await stream.ReadAsync(buffer.AsMemory(total, MaxResponseBytes - total), ct))
                > 0
        )
            total += read;

        if (total == MaxResponseBytes && await stream.ReadAsync(new byte[1], ct) > 0)
            return (true, string.Empty);

        return (false, Encoding.UTF8.GetString(buffer, 0, total));
    }
}
