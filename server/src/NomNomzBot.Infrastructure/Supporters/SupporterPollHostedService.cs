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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Supporters.Services;
using NomNomzBot.Domain.Supporters.Entities;

namespace NomNomzBot.Infrastructure.Supporters;

/// <summary>
/// The <c>poll</c> ingress runner (supporter-events.md §0 D3): every ~15 s — the cadence DonorDrive's public
/// API asks of pollers — it walks every enabled poll-mode <see cref="SupporterConnection"/>, opens its sealed
/// feed URL (the connection's AEAD secret; for DonorDrive the public participant <c>/donations</c> URL), and
/// conditional-GETs it with <c>If-None-Match</c> so an unchanged feed costs one 304. Fresh items feed the
/// single ingest path oldest-first (alerts fire in donation order) — dedup on the provider transaction id
/// makes every re-poll idempotent. Runs once across replicas via <see cref="IRunOnceGuard"/>; one provider's
/// failure marks that connection <c>error</c> and never blocks the rest.
/// </summary>
public sealed class SupporterPollHostedService : BackgroundService
{
    internal const string HttpClientName = "supporter-poll";

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LeaseTtl = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<SupporterPollHostedService> _logger;

    // Conditional-GET state per connection — in-memory on purpose: a restart just pays one full fetch,
    // and the ingest dedup absorbs the re-delivered items.
    private readonly Dictionary<Guid, string> _etagsByConnection = new();

    public SupporterPollHostedService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        TimeProvider clock,
        ILogger<SupporterPollHostedService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TickInterval, _clock);
        try
        {
            do
            {
                try
                {
                    await PollOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Supporter poll tick failed");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Host shutdown.
        }
    }

    // Internal (not private) so tests can drive a single deterministic tick —
    // InternalsVisibleTo(NomNomzBot.Infrastructure.Tests) is already wired for exactly this seam.
    internal async Task PollOnceAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IRunOnceGuard guard = scope.ServiceProvider.GetRequiredService<IRunOnceGuard>();
        await using IAsyncDisposable? lease = await guard.TryAcquireAsync(
            "supporters-poll",
            LeaseTtl,
            ct
        );
        if (lease is null)
            return; // another instance is polling.

        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        ITokenProtector protector = scope.ServiceProvider.GetRequiredService<ITokenProtector>();
        ISupporterIngestService ingest =
            scope.ServiceProvider.GetRequiredService<ISupporterIngestService>();

        List<SupporterConnection> connections = await db
            .SupporterConnections.Where(c =>
                c.IsEnabled && c.ConnectionMode == "poll" && c.DeletedAt == null
            )
            .ToListAsync(ct);
        if (connections.Count == 0)
            return;

        foreach (SupporterConnection connection in connections)
            await PollConnectionAsync(connection, protector, ingest, ct);

        await db.SaveChangesAsync(ct); // persists any status flips from failed polls
    }

    private async Task PollConnectionAsync(
        SupporterConnection connection,
        ITokenProtector protector,
        ISupporterIngestService ingest,
        CancellationToken ct
    )
    {
        string? feedUrl = await protector.TryUnprotectAsync(
            connection.AuthSecretCipher,
            SupporterConnectionService.SecretContext(
                connection.BroadcasterId,
                connection.SourceKey
            ),
            ct
        );
        if (
            string.IsNullOrWhiteSpace(feedUrl)
            || !Uri.TryCreate(feedUrl, UriKind.Absolute, out Uri? feed)
            || feed.Scheme != Uri.UriSchemeHttps
        )
        {
            connection.Status = "error";
            _logger.LogWarning(
                "Supporter poll for {Source} on {Channel} has no usable https feed URL — set the connection secret to the provider's donations URL.",
                connection.SourceKey,
                connection.BroadcasterId
            );
            return;
        }

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, feed);
            if (_etagsByConnection.TryGetValue(connection.Id, out string? etag))
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);

            HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
            using HttpResponseMessage response = await client.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotModified)
                return; // unchanged feed — the whole point of the ETag.

            if (!response.IsSuccessStatusCode)
            {
                connection.Status = "error";
                _logger.LogWarning(
                    "Supporter poll for {Source} on {Channel} got HTTP {Status} from the feed.",
                    connection.SourceKey,
                    connection.BroadcasterId,
                    (int)response.StatusCode
                );
                return;
            }

            string body = await response.Content.ReadAsStringAsync(ct);
            JArray items = ParseItems(body);

            // Oldest first: DonorDrive feeds are newest-first, and alerts should fire in donation order.
            foreach (JToken item in items.Reverse())
            {
                Result ingested = await ingest.IngestAsync(
                    connection.BroadcasterId,
                    connection.SourceKey,
                    item.ToString(Formatting.None),
                    ct
                );
                if (ingested.IsFailure)
                    _logger.LogWarning(
                        "Supporter poll ingest failed for {Source} on {Channel}: {Error}",
                        connection.SourceKey,
                        connection.BroadcasterId,
                        ingested.ErrorMessage
                    );
            }

            if (response.Headers.ETag is { Tag: string newTag })
                _etagsByConnection[connection.Id] = newTag;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            connection.Status = "error";
            _logger.LogWarning(
                ex,
                "Supporter poll for {Source} on {Channel} failed.",
                connection.SourceKey,
                connection.BroadcasterId
            );
        }
    }

    /// <summary>The feed as an item array — bare (DonorDrive) or under a single wrapping array property.</summary>
    private static JArray ParseItems(string body)
    {
        JToken parsed = JToken.Parse(body);
        if (parsed is JArray array)
            return array;
        if (parsed is JObject obj)
            foreach (JProperty property in obj.Properties())
                if (property.Value is JArray nested)
                    return nested;
        return new JArray();
    }
}
