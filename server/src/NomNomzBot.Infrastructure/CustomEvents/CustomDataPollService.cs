// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Infrastructure.Sandbox;

namespace NomNomzBot.Infrastructure.CustomEvents;

/// <summary>
/// The <c>poll</c> ingress fetcher (custom-events.md §6). For each enabled poll-kind source whose interval has
/// elapsed since <c>LastReceivedAt</c>, it validates the user-supplied <c>EndpointUrl</c> host against an enabled
/// H.7 <c>HttpEgressAllowlist</c> row for that channel (the SSRF boundary), fetches through the shared
/// SSRF-hardened <see cref="EgressHttpClient"/> (resolve-then-pin, https-only, no redirects), and — on a 2xx with a
/// bounded body — hands the raw payload to the single <see cref="ICustomDataIngestService"/> path (which does the
/// JSONPath extraction, publishes the event, updates the cache, and stamps <c>LastReceivedAt</c>). A non-allowlisted
/// host is skipped without any fetch; every other fault is logged and isolated per source.
/// </summary>
internal sealed class CustomDataPollService : ICustomDataPollService
{
    /// <summary>Response-body read cap — mirrors the ingest raw cap (custom-events.md D4).</summary>
    private const int MaxResponseBytes = 64 * 1024;

    /// <summary>Fallback cadence when a poll source has no explicit interval (defensive; poll sources set one).</summary>
    private const int DefaultPollIntervalSeconds = 60;

    private const string SecretProvider = "customdata";

    private readonly IApplicationDbContext _db;
    private readonly ITokenProtector _tokenProtector;
    private readonly ICustomDataIngestService _ingest;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<CustomDataPollService> _logger;

    public CustomDataPollService(
        IApplicationDbContext db,
        ITokenProtector tokenProtector,
        ICustomDataIngestService ingest,
        IHttpClientFactory httpClientFactory,
        TimeProvider clock,
        ILogger<CustomDataPollService> logger
    )
    {
        _db = db;
        _tokenProtector = tokenProtector;
        _ingest = ingest;
        _httpClientFactory = httpClientFactory;
        _clock = clock;
        _logger = logger;
    }

    public async Task PollDueSourcesAsync(CancellationToken ct = default)
    {
        DateTime now = _clock.GetUtcNow().UtcDateTime;

        List<CustomDataSource> sources = await _db
            .CustomDataSources.Where(s =>
                s.IsEnabled && s.SourceKind == "poll" && s.DeletedAt == null
            )
            .ToListAsync(ct);

        foreach (CustomDataSource source in sources)
        {
            if (!IsDue(source, now))
                continue;

            try
            {
                await PollSourceAsync(source, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Custom data poll for source '{Source}' on channel {Channel} faulted.",
                    source.Name,
                    source.BroadcasterId
                );
            }
        }
    }

    /// <summary>Due when never received, or the configured interval has elapsed since the last receive.</summary>
    private static bool IsDue(CustomDataSource source, DateTime now)
    {
        if (source.LastReceivedAt is null)
            return true;

        int intervalSeconds = source.PollIntervalSeconds ?? DefaultPollIntervalSeconds;
        return now - source.LastReceivedAt.Value >= TimeSpan.FromSeconds(intervalSeconds);
    }

    private async Task PollSourceAsync(CustomDataSource source, CancellationToken ct)
    {
        if (
            string.IsNullOrWhiteSpace(source.EndpointUrl)
            || !Uri.TryCreate(source.EndpointUrl, UriKind.Absolute, out Uri? endpoint)
        )
        {
            _logger.LogWarning(
                "Custom data poll source '{Source}' on channel {Channel} has no usable absolute endpoint URL — skipped.",
                source.Name,
                source.BroadcasterId
            );
            return;
        }

        // ── SSRF gate ──────────────────────────────────────────────────────────────────────────
        // The endpoint host is user-supplied, so it MUST match an enabled egress-allowlist row for this
        // channel before any fetch. This mirrors the outbound-webhook Fqdn match (the single H.7 boundary);
        // the EgressHttpClient adds resolve-then-pin + non-public-IP rejection as defense in depth.
        string host = endpoint.Host;
        bool allowed = await _db.HttpEgressAllowlists.AnyAsync(
            a =>
                a.BroadcasterId == source.BroadcasterId
                && a.Fqdn == host
                && a.IsEnabled
                && a.DeletedAt == null,
            ct
        );
        if (!allowed)
        {
            _logger.LogWarning(
                "Custom data poll source '{Source}' on channel {Channel} targets non-allowlisted host '{Host}' — skipped (SSRF egress gate).",
                source.Name,
                source.BroadcasterId,
                host
            );
            return;
        }

        // Unseal the optional bearer credential with the exact context CustomDataSourceService sealed it under.
        string? authSecret = source.AuthSecretCipher is null
            ? null
            : await _tokenProtector.TryUnprotectAsync(
                source.AuthSecretCipher,
                new TokenProtectionContext(
                    source.BroadcasterId.ToString(),
                    SecretProvider,
                    source.Id.ToString()
                ),
                ct
            );

        using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
        if (!string.IsNullOrWhiteSpace(authSecret))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authSecret);

        HttpClient client = _httpClientFactory.CreateClient(EgressHttpClient.Name);
        using HttpResponseMessage response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Custom data poll source '{Source}' on channel {Channel} got HTTP {Status} from the endpoint.",
                source.Name,
                source.BroadcasterId,
                (int)response.StatusCode
            );
            return;
        }

        (bool oversize, string body) = await ReadBoundedAsync(response, ct);
        if (oversize)
        {
            _logger.LogWarning(
                "Custom data poll source '{Source}' on channel {Channel} returned a body over the {Cap} byte cap — skipped.",
                source.Name,
                source.BroadcasterId,
                MaxResponseBytes
            );
            return;
        }

        if (body.Length == 0)
            return; // empty 2xx — nothing to ingest, just wait for the next interval.

        Result ingested = await _ingest.IngestAsync(source.BroadcasterId, source.Name, body, ct);
        if (ingested.IsFailure)
            _logger.LogWarning(
                "Custom data poll ingest failed for source '{Source}' on channel {Channel}: {Error}",
                source.Name,
                source.BroadcasterId,
                ingested.ErrorMessage
            );
    }

    /// <summary>
    /// Reads the response body into a fixed cap. Returns <c>oversize=true</c> (and no body) when the payload
    /// exceeds the cap, so a truncated fragment is never fed to ingest as if it were a whole payload.
    /// </summary>
    private static async Task<(bool Oversize, string Body)> ReadBoundedAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        // Fast reject when the server declares an oversize body up front — no need to buffer.
        if (response.Content.Headers.ContentLength is long declared && declared > MaxResponseBytes)
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

        // The buffer filled to the cap and there is still more on the wire → oversize.
        if (total == MaxResponseBytes && await stream.ReadAsync(new byte[1], ct) > 0)
            return (true, string.Empty);

        return (false, Encoding.UTF8.GetString(buffer, 0, total));
    }
}
