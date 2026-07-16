// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Chat.Kick;

/// <summary>
/// The Kick chat-READ reconcile (slice 3b-2c-2): for every streamer who CONNECTED Kick (a tenant-scoped
/// <c>kick</c> integration connection — the deliberate opt-in signal; identity-plane login connections
/// are NOT enough), it provisions their Kick presence as its own tenant <c>Channel</c> row
/// (<see cref="IPlatformChannelProvisioner"/>, the stable Guid the webhook ingest resolves) and ensures
/// the full wanted webhook subscription set (<see cref="WantedEvents"/>) exists on their token — only the
/// missing events are created, so channels subscribed before an event shipped self-heal. Declarative + idempotent per
/// 5-minute tick, mirroring <c>BotLifecycleService</c>; a missing <c>events:subscribe</c> scope backs the
/// connection off for 30 minutes (self-heals on re-grant — same posture as EventSub's scope gate) instead
/// of hammering guaranteed 403s.
/// </summary>
public sealed class KickEventSubscriptionWorker : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MissingScopeBackoff = TimeSpan.FromMinutes(30);

    /// <summary>The webhook events every connected Kick channel must carry — Kick's FULL verified event
    /// surface: the chat READ leg, the live tracker behind the dashboard's <c>platformsLive</c>, and the
    /// community/monetization events the ingest translates onto the canonical bus.</summary>
    private static readonly KickEventRequest[] WantedEvents =
    [
        new("chat.message.sent", 1),
        new("channel.followed", 1),
        new("channel.subscription.new", 1),
        new("channel.subscription.renewal", 1),
        new("channel.subscription.gifts", 1),
        new("channel.reward.redemption.updated", 1),
        new("livestream.status.updated", 1),
        new("livestream.metadata.updated", 1),
        new("moderation.banned", 1),
        new("kicks.gifted", 1),
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IKickApiClient _client;
    private readonly TimeProvider _clock;
    private readonly ILogger<KickEventSubscriptionWorker> _logger;

    // Per-tenant scope-failure backoff, touched only from the single tick loop (and tests).
    private readonly Dictionary<Guid, DateTime> _backoffUntilUtc = [];

    public KickEventSubscriptionWorker(
        IServiceScopeFactory scopeFactory,
        IKickApiClient client,
        TimeProvider clock,
        ILogger<KickEventSubscriptionWorker> logger
    )
    {
        _scopeFactory = scopeFactory;
        _client = client;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("KickEventSubscriptionWorker starting.");
        using PeriodicTimer timer = new(TickInterval, _clock);
        try
        {
            do
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Kick subscription reconcile tick failed");
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
    internal async Task TickAsync(CancellationToken ct)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        // The opt-in signal: a tenant-scoped kick connection written by the integrations connect flow.
        var connections = await db
            .IntegrationConnections.Where(c =>
                c.Provider == AuthEnums.IntegrationProvider.Kick
                && c.BroadcasterId != null
                && c.Status != "revoked"
                && c.ProviderAccountId != null
            )
            .Select(c => new
            {
                PrimaryChannelId = c.BroadcasterId!.Value,
                KickAccountId = c.ProviderAccountId!,
                c.ProviderAccountName,
            })
            .ToListAsync(ct);

        foreach (var connection in connections)
        {
            try
            {
                await ReconcileOneAsync(
                    scope.ServiceProvider,
                    connection.PrimaryChannelId,
                    connection.KickAccountId,
                    connection.ProviderAccountName,
                    ct
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Kick reconcile failed for primary channel {PrimaryChannelId}",
                    connection.PrimaryChannelId
                );
            }
        }
    }

    private async Task ReconcileOneAsync(
        IServiceProvider services,
        Guid primaryChannelId,
        string kickAccountId,
        string? displayName,
        CancellationToken ct
    )
    {
        IApplicationDbContext db = services.GetRequiredService<IApplicationDbContext>();
        Channel? primary = await db.Channels.FirstOrDefaultAsync(c => c.Id == primaryChannelId, ct);
        if (primary is null)
            return;

        // The stable tenant every persisted Kick message and hub push rides under — provisioned BEFORE
        // the subscription so a first webhook always resolves.
        IPlatformChannelProvisioner provisioner =
            services.GetRequiredService<IPlatformChannelProvisioner>();
        Guid kickTenantId = await provisioner.GetOrCreateAsync(
            primary.OwnerUserId,
            AuthEnums.Platform.Kick,
            kickAccountId,
            displayName ?? kickAccountId,
            ct
        );

        DateTime now = _clock.GetUtcNow().UtcDateTime;
        if (_backoffUntilUtc.TryGetValue(kickTenantId, out DateTime until) && now < until)
            return;

        IKickAccessTokenProvider tokens = services.GetRequiredService<IKickAccessTokenProvider>();
        KickAccess? access = await tokens.GetAsync(kickTenantId, ct);
        if (access is null)
            return; // no usable token — the provider already logged/marked it.

        Result<IReadOnlyList<KickEventSubscription>> listed =
            await _client.ListEventSubscriptionsAsync(access.AccessToken, ct);
        if (listed.IsFailure)
        {
            HandleFailure(kickTenantId, listed.ErrorCode, listed.ErrorMessage, now);
            return;
        }

        List<KickEventRequest> missing =
        [
            .. WantedEvents.Where(wanted =>
                !listed.Value.Any(s => s.Event == wanted.Name && s.Method == "webhook")
            ),
        ];
        if (missing.Count == 0)
            return;

        Result created = await _client.SubscribeAsync(access.AccessToken, missing, ct);
        if (created.IsFailure)
        {
            HandleFailure(kickTenantId, created.ErrorCode, created.ErrorMessage, now);
            return;
        }

        _logger.LogInformation(
            "Kick webhook events {Events} subscribed for tenant {KickTenantId} (account {KickAccountId})",
            string.Join(", ", missing.Select(e => e.Name)),
            kickTenantId,
            kickAccountId
        );
    }

    private void HandleFailure(Guid kickTenantId, string? code, string? message, DateTime now)
    {
        // A missing events:subscribe grant is a guaranteed 403 until the streamer re-connects with the
        // kick.chat scope-set — back off long instead of retrying every tick; anything else is transient
        // and simply retries next tick.
        if (code == "MISSING_SCOPE")
        {
            _backoffUntilUtc[kickTenantId] = now + MissingScopeBackoff;
            _logger.LogWarning(
                "Kick subscription blocked for tenant {KickTenantId}: missing events:subscribe (reconnect with the kick.chat scopes) — backing off",
                kickTenantId
            );
            return;
        }

        _logger.LogWarning(
            "Kick subscription reconcile failed for tenant {KickTenantId}: {Error} ({Code})",
            kickTenantId,
            message,
            code
        );
    }
}
