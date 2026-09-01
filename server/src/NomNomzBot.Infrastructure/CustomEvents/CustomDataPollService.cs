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

    /// <summary>Consecutive fetch failures at which a source auto-disables — mirrors
    /// <c>OutboundWebhookDispatcher.AutoDisableThreshold</c> (S099a) for consistency across the two
    /// egress-reliability surfaces.</summary>
    private const int AutoDisableThreshold = 20;

    private const string SecretProvider = "customdata";

    /// <summary>The outcome of one fetch attempt, used to update the source's reliability fields.</summary>
    private enum PollOutcome
    {
        /// <summary>Config-level reject (malformed URL, non-allowlisted host) — no fetch was attempted, so the
        /// failure counter and backoff are left untouched.</summary>
        Skipped,
        Success,
        Failure,
    }

    private readonly IApplicationDbContext _db;
    private readonly ITokenProtector _tokenProtector;
    private readonly ICustomDataIngestService _ingest;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICustomDataPollAttemptTracker _attempts;
    private readonly TimeProvider _clock;
    private readonly ILogger<CustomDataPollService> _logger;

    public CustomDataPollService(
        IApplicationDbContext db,
        ITokenProtector tokenProtector,
        ICustomDataIngestService ingest,
        IHttpClientFactory httpClientFactory,
        ICustomDataPollAttemptTracker attempts,
        TimeProvider clock,
        ILogger<CustomDataPollService> logger
    )
    {
        _db = db;
        _tokenProtector = tokenProtector;
        _ingest = ingest;
        _httpClientFactory = httpClientFactory;
        _attempts = attempts;
        _clock = clock;
        _logger = logger;
    }

    public async Task PollDueSourcesAsync(CancellationToken ct = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();

        List<CustomDataSource> sources = await _db
            .CustomDataSources.Where(s =>
                s.IsEnabled && s.SourceKind == "poll" && s.DeletedAt == null
            )
            .ToListAsync(ct);

        foreach (CustomDataSource source in sources)
        {
            if (!IsDue(source, now))
                continue;

            // Stamp the attempt BEFORE fetching so a fault (or an SSRF-gate skip) still counts as an attempt —
            // otherwise a source that never reaches a success would be re-attempted every ~5 s scan tick.
            _attempts.RecordAttempt(source.Id, now);
            source.LastAttemptAt = now.UtcDateTime;

            PollOutcome outcome;
            string? errorMessage = null;
            try
            {
                (outcome, errorMessage) = await PollSourceAsync(source, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                outcome = PollOutcome.Failure;
                errorMessage = ex.Message;
                _logger.LogWarning(
                    ex,
                    "Custom data poll for source '{Source}' on channel {Channel} faulted.",
                    source.Name,
                    source.BroadcasterId
                );
            }

            ApplyOutcome(source, outcome, errorMessage, now);

            // Success already saved LastReceivedAt via the ingest path (same DbContext instance), but a
            // Failure/Skipped outcome — and the LastAttemptAt stamp above — still need to be flushed.
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Applies the S100a reliability bookkeeping to the source's tracked entity: resets the failure streak on
    /// success, or increments it and schedules a capped+jittered backoff on failure — auto-disabling the source
    /// once <see cref="AutoDisableThreshold"/> consecutive failures is reached, mirroring
    /// <c>OutboundWebhookDispatcher.ApplyOutcomeAsync</c> (S099a). A <see cref="PollOutcome.Skipped"/> outcome
    /// (config-level reject, no fetch attempted) leaves the failure counter and backoff untouched.
    /// </summary>
    private void ApplyOutcome(
        CustomDataSource source,
        PollOutcome outcome,
        string? errorMessage,
        DateTimeOffset now
    )
    {
        switch (outcome)
        {
            case PollOutcome.Success:
                source.ConsecutiveFailureCount = 0;
                source.LastError = null;
                source.NextRetryAt = null;
                break;

            case PollOutcome.Failure:
                source.ConsecutiveFailureCount++;
                source.LastError = errorMessage is { Length: > 1000 }
                    ? errorMessage[..1000]
                    : errorMessage;

                if (source.ConsecutiveFailureCount >= AutoDisableThreshold)
                {
                    source.IsEnabled = false;
                    source.DisabledAt = now.UtcDateTime;
                    source.DisabledReason = "Too many consecutive fetch failures.";
                    source.NextRetryAt = null;
                    _logger.LogWarning(
                        "Custom data poll source '{Source}' on channel {Channel} auto-disabled after {Count} consecutive failures.",
                        source.Name,
                        source.BroadcasterId,
                        source.ConsecutiveFailureCount
                    );
                }
                else
                {
                    source.NextRetryAt = now.UtcDateTime.Add(
                        CustomDataPollBackoffPolicy.ComputeDelay(source.ConsecutiveFailureCount)
                    );
                }
                break;

            case PollOutcome.Skipped:
            default:
                break;
        }
    }

    /// <summary>
    /// Due when the configured interval has elapsed since the last <em>attempt</em> — where the last attempt is the
    /// later of the DB <c>LastReceivedAt</c> (stamped only on a successful ingest) and the in-memory last-attempt
    /// stamp (recorded on every attempt, success or fail) — AND, when a S100a backoff is pending
    /// (<see cref="CustomDataSource.NextRetryAt"/> set by a prior failure), the backoff window has also elapsed.
    /// Gating on attempts, not just successes, keeps a persistently-failing source on its interval instead of
    /// hammering the host every scan tick; the backoff check on top of that prevents a short poll interval from
    /// overriding a capped+jittered retry delay after repeated failures.
    /// </summary>
    private bool IsDue(CustomDataSource source, DateTimeOffset now)
    {
        DateTimeOffset? lastReceived = source.LastReceivedAt is null
            ? null
            : new DateTimeOffset(
                DateTime.SpecifyKind(source.LastReceivedAt.Value, DateTimeKind.Utc)
            );
        DateTimeOffset? lastActivity = Latest(lastReceived, _attempts.LastAttempt(source.Id));

        bool dueByInterval;
        if (lastActivity is null)
            dueByInterval = true;
        else
        {
            int intervalSeconds = source.PollIntervalSeconds ?? DefaultPollIntervalSeconds;
            dueByInterval = now - lastActivity.Value >= TimeSpan.FromSeconds(intervalSeconds);
        }

        if (!dueByInterval)
            return false;

        if (source.NextRetryAt is null)
            return true;

        DateTimeOffset nextRetryAt = new(
            DateTime.SpecifyKind(source.NextRetryAt.Value, DateTimeKind.Utc)
        );
        return now >= nextRetryAt;
    }

    /// <summary>The later of two optional instants (either may be null).</summary>
    private static DateTimeOffset? Latest(DateTimeOffset? a, DateTimeOffset? b)
    {
        if (a is null)
            return b;
        if (b is null)
            return a;
        return a.Value >= b.Value ? a : b;
    }

    private async Task<(PollOutcome Outcome, string? ErrorMessage)> PollSourceAsync(
        CustomDataSource source,
        CancellationToken ct
    )
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
            return (PollOutcome.Skipped, null);
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
            return (PollOutcome.Skipped, null);
        }

        // Unseal the optional bearer credential with the exact context CustomDataSourceService sealed it under.
        string? authSecret = source.AuthSecretCipher is null
            ? null
            : await _tokenProtector.TryUnprotectAsync(
                source.AuthSecretCipher,
                new(source.BroadcasterId.ToString(), SecretProvider, source.Id.ToString()),
                ct
            );

        using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
        if (!string.IsNullOrWhiteSpace(authSecret))
            request.Headers.Authorization = new("Bearer", authSecret);

        HttpClient client = _httpClientFactory.CreateClient(EgressHttpClient.Name);
        using HttpResponseMessage response = await client.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            string message = $"HTTP {(int)response.StatusCode} from the endpoint.";
            _logger.LogWarning(
                "Custom data poll source '{Source}' on channel {Channel} got {Message}",
                source.Name,
                source.BroadcasterId,
                message
            );
            return (PollOutcome.Failure, message);
        }

        (bool oversize, string body) = await ReadBoundedAsync(response, ct);
        if (oversize)
        {
            string message = $"Response body exceeded the {MaxResponseBytes} byte cap.";
            _logger.LogWarning(
                "Custom data poll source '{Source}' on channel {Channel} returned a body over the {Cap} byte cap — skipped.",
                source.Name,
                source.BroadcasterId,
                MaxResponseBytes
            );
            return (PollOutcome.Failure, message);
        }

        if (body.Length == 0)
            return (PollOutcome.Success, null); // empty 2xx — nothing to ingest, just wait for the next interval.

        Result ingested = await _ingest.IngestAsync(source.BroadcasterId, source.Name, body, ct);
        if (ingested.IsFailure)
        {
            _logger.LogWarning(
                "Custom data poll ingest failed for source '{Source}' on channel {Channel}: {Error}",
                source.Name,
                source.BroadcasterId,
                ingested.ErrorMessage
            );
            return (PollOutcome.Failure, ingested.ErrorMessage);
        }

        return (PollOutcome.Success, null);
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

        // The buffer filled to the cap and there is still more on the wire → oversize.
        if (total == MaxResponseBytes && await stream.ReadAsync(new byte[1], ct) > 0)
            return (true, string.Empty);

        return (false, Encoding.UTF8.GetString(buffer, 0, total));
    }
}
