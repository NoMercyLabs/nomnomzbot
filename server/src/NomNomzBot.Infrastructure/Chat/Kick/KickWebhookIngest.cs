// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Chat.Kick;

/// <summary>
/// <see cref="IKickWebhookIngest"/>: routes every VERIFIED Kick delivery by event type and translates it
/// into the platform's ONE substrate. Chat becomes <see cref="ChatMessageReceivedEvent"/>
/// (<c>Provider = kick</c>, deduped against the persisted <c>ChatMessages</c> under Kick's redeliveries);
/// follows / subs / gift subs / kicks / reward redemptions publish the SAME canonical domain events their
/// Twitch EventSub twins do, so alerts, engagement earning, and the dashboard broadcasts fire with zero
/// Kick-specific consumers; <c>livestream.status.updated</c> stamps the tenant's <c>Channel.IsLive</c>
/// (+ title) behind the dashboard's <c>platformsLive</c>, and <c>livestream.metadata.updated</c> rides the
/// canonical <see cref="ChannelUpdatedEvent"/> (its handler persists title/category). The tenant resolves
/// by the broadcaster's numeric Kick id against the kick-provider <c>Channel</c> row (provisioned by the
/// reconcile worker on connect); a delivery for an unknown broadcaster is skipped — the same posture as
/// EventSub's unknown-channel guard. Kick has a single subscription tier, mapped to the canonical base
/// tier <c>"1000"</c>.
/// </summary>
public sealed class KickWebhookIngest : IKickWebhookIngest
{
    /// <summary>Kick subs are untiered — every one maps to the canonical base tier.</summary>
    private const string KickBaseTier = "1000";

    private readonly IApplicationDbContext _db;
    private readonly IEventBus _bus;
    private readonly IChannelRegistry _registry;
    private readonly TimeProvider _clock;
    private readonly ILogger<KickWebhookIngest> _logger;

    public KickWebhookIngest(
        IApplicationDbContext db,
        IEventBus bus,
        IChannelRegistry registry,
        TimeProvider clock,
        ILogger<KickWebhookIngest> logger
    )
    {
        _db = db;
        _bus = bus;
        _registry = registry;
        _clock = clock;
        _logger = logger;
    }

    public Task HandleAsync(
        string eventType,
        string rawBody,
        CancellationToken cancellationToken = default
    ) =>
        eventType switch
        {
            "chat.message.sent" => HandleChatMessageAsync(rawBody, cancellationToken),
            "livestream.status.updated" => HandleLivestreamStatusAsync(rawBody, cancellationToken),
            "livestream.metadata.updated" => HandleLivestreamMetadataAsync(
                rawBody,
                cancellationToken
            ),
            "channel.followed" => HandleFollowedAsync(rawBody, cancellationToken),
            "channel.subscription.new" => HandleSubscriptionNewAsync(rawBody, cancellationToken),
            "channel.subscription.renewal" => HandleSubscriptionRenewalAsync(
                rawBody,
                cancellationToken
            ),
            "channel.subscription.gifts" => HandleSubscriptionGiftsAsync(
                rawBody,
                cancellationToken
            ),
            "channel.reward.redemption.updated" => HandleRewardRedemptionAsync(
                rawBody,
                cancellationToken
            ),
            "moderation.banned" => HandleModerationBannedAsync(rawBody, cancellationToken),
            "kicks.gifted" => HandleKicksGiftedAsync(rawBody, cancellationToken),
            // An authenticated delivery with no consumer — deliberately ignored, still acknowledged.
            _ => Task.CompletedTask,
        };

    // ─── chat.message.sent ───────────────────────────────────────────────────

    private async Task HandleChatMessageAsync(string rawBody, CancellationToken ct)
    {
        KickChatMessagePayload? payload = Parse<KickChatMessagePayload>(
            rawBody,
            "chat.message.sent"
        );
        if (
            payload?.MessageId is not { Length: > 0 } messageId
            || payload.Broadcaster?.UserId is not { } broadcasterKickId
            || payload.Sender?.UserId is not { } senderKickId
        )
        {
            WarnMissingIdentity(payload, "chat.message.sent");
            return;
        }

        Guid tenantId = await ResolveTenantIdAsync(broadcasterKickId, ct);
        if (tenantId == Guid.Empty)
            return;

        // Blacklisted chatters (J.12) are dropped HERE, before the bus fan-out.
        string senderKickIdText = senderKickId.ToString(CultureInfo.InvariantCulture);
        if (
            _registry
                .Get(tenantId)
                ?.ModerationStandings.GetValueOrDefault($"kick:{senderKickIdText}")
            == Domain.Moderation.Entities.ModerationStanding.Blacklisted
        )
            return;

        // Kick retries undelivered webhooks — anything already persisted has already been broadcast.
        bool seen = await _db.ChatMessages.AnyAsync(m => m.Id == messageId, ct);
        if (seen)
            return;

        IReadOnlyList<string> badgeTypes =
        [
            .. (payload.Sender.Identity?.Badges ?? [])
                .Select(b => b.Type ?? string.Empty)
                .Where(t => t.Length > 0),
        ];

        await _bus.PublishAsync(
            new ChatMessageReceivedEvent
            {
                BroadcasterId = tenantId,
                Provider = AuthEnums.Platform.Kick,
                OccurredAt = payload.CreatedAt ?? _clock.GetUtcNow(),
                MessageId = messageId,
                TwitchBroadcasterId = KickId(broadcasterKickId),
                UserId = KickId(senderKickId),
                UserDisplayName = payload.Sender.Username ?? string.Empty,
                UserLogin = Login(payload.Sender),
                Message = payload.Content ?? string.Empty,
                Fragments =
                [
                    new ChatMessageFragment
                    {
                        Type = "text",
                        Text = payload.Content ?? string.Empty,
                    },
                ],
                Badges = [],
                IsSubscriber = badgeTypes.Contains("subscriber", StringComparer.OrdinalIgnoreCase),
                IsVip = badgeTypes.Contains("vip", StringComparer.OrdinalIgnoreCase),
                IsModerator = badgeTypes.Contains("moderator", StringComparer.OrdinalIgnoreCase),
                IsBroadcaster =
                    senderKickId == broadcasterKickId
                    || badgeTypes.Contains("broadcaster", StringComparer.OrdinalIgnoreCase),
            },
            ct
        );
    }

    // ─── livestream.status.updated — the live tracker behind platformsLive ──

    private async Task HandleLivestreamStatusAsync(string rawBody, CancellationToken ct)
    {
        KickLivestreamStatusPayload? payload = Parse<KickLivestreamStatusPayload>(
            rawBody,
            "livestream.status.updated"
        );
        if (
            payload?.Broadcaster?.UserId is not { } broadcasterKickId
            || payload.IsLive is not { } isLive
        )
        {
            WarnMissingIdentity(payload, "livestream.status.updated");
            return;
        }

        Channel? tenant = await ResolveTenantAsync(broadcasterKickId, ct);
        if (tenant is null)
            return;

        // Idempotent under Kick's redeliveries: stamping the same state twice is a no-op write.
        tenant.IsLive = isLive;
        if (!string.IsNullOrWhiteSpace(payload.Title))
            tenant.Title = payload.Title;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Kick tenant {TenantId} is now {State}",
            tenant.Id,
            isLive ? "LIVE" : "OFFLINE"
        );
    }

    // ─── livestream.metadata.updated → canonical ChannelUpdatedEvent ─────────

    private async Task HandleLivestreamMetadataAsync(string rawBody, CancellationToken ct)
    {
        KickLivestreamMetadataPayload? payload = Parse<KickLivestreamMetadataPayload>(
            rawBody,
            "livestream.metadata.updated"
        );
        if (payload?.Broadcaster?.UserId is not { } broadcasterKickId)
        {
            WarnMissingIdentity(payload, "livestream.metadata.updated");
            return;
        }

        Guid tenantId = await ResolveTenantIdAsync(broadcasterKickId, ct);
        if (tenantId == Guid.Empty)
            return;

        // ChannelUpdatedHandler persists title + game onto the tenant row — same path as Twitch.
        await _bus.PublishAsync(
            new ChannelUpdatedEvent
            {
                BroadcasterId = tenantId,
                OccurredAt = _clock.GetUtcNow(),
                BroadcasterDisplayName = payload.Broadcaster.Username ?? string.Empty,
                NewTitle = payload.Metadata?.Title ?? string.Empty,
                NewGameName = payload.Metadata?.Category?.Name ?? string.Empty,
            },
            ct
        );
    }

    // ─── channel.followed → canonical FollowEvent ────────────────────────────

    private async Task HandleFollowedAsync(string rawBody, CancellationToken ct)
    {
        KickFollowedPayload? payload = Parse<KickFollowedPayload>(rawBody, "channel.followed");
        if (
            payload?.Broadcaster?.UserId is not { } broadcasterKickId
            || payload.Follower?.UserId is not { } followerKickId
        )
        {
            WarnMissingIdentity(payload, "channel.followed");
            return;
        }

        Guid tenantId = await ResolveTenantIdAsync(broadcasterKickId, ct);
        if (tenantId == Guid.Empty)
            return;

        DateTimeOffset now = _clock.GetUtcNow();
        await _bus.PublishAsync(
            new FollowEvent
            {
                BroadcasterId = tenantId,
                OccurredAt = now,
                UserId = KickId(followerKickId),
                UserDisplayName = payload.Follower.Username ?? string.Empty,
                UserLogin = Login(payload.Follower),
                FollowedAt = now, // the payload carries no follow timestamp
            },
            ct
        );
    }

    // ─── channel.subscription.new / .renewal → canonical sub events ──────────

    private async Task HandleSubscriptionNewAsync(string rawBody, CancellationToken ct)
    {
        KickSubscriptionPayload? payload = Parse<KickSubscriptionPayload>(
            rawBody,
            "channel.subscription.new"
        );
        if (
            payload?.Broadcaster?.UserId is not { } broadcasterKickId
            || payload.Subscriber?.UserId is not { } subscriberKickId
        )
        {
            WarnMissingIdentity(payload, "channel.subscription.new");
            return;
        }

        Guid tenantId = await ResolveTenantIdAsync(broadcasterKickId, ct);
        if (tenantId == Guid.Empty)
            return;

        await _bus.PublishAsync(
            new NewSubscriptionEvent
            {
                BroadcasterId = tenantId,
                OccurredAt = payload.CreatedAt ?? _clock.GetUtcNow(),
                UserId = KickId(subscriberKickId),
                UserDisplayName = payload.Subscriber.Username ?? string.Empty,
                Tier = KickBaseTier,
            },
            ct
        );
    }

    private async Task HandleSubscriptionRenewalAsync(string rawBody, CancellationToken ct)
    {
        KickSubscriptionPayload? payload = Parse<KickSubscriptionPayload>(
            rawBody,
            "channel.subscription.renewal"
        );
        if (
            payload?.Broadcaster?.UserId is not { } broadcasterKickId
            || payload.Subscriber?.UserId is not { } subscriberKickId
        )
        {
            WarnMissingIdentity(payload, "channel.subscription.renewal");
            return;
        }

        Guid tenantId = await ResolveTenantIdAsync(broadcasterKickId, ct);
        if (tenantId == Guid.Empty)
            return;

        await _bus.PublishAsync(
            new ResubscriptionEvent
            {
                BroadcasterId = tenantId,
                OccurredAt = payload.CreatedAt ?? _clock.GetUtcNow(),
                UserId = KickId(subscriberKickId),
                UserDisplayName = payload.Subscriber.Username ?? string.Empty,
                Tier = KickBaseTier,
                CumulativeMonths = payload.Duration ?? 0,
                StreakMonths = 0, // Kick does not report streaks — never invent one.
                Message = null, // Kick renewals carry no resub message.
            },
            ct
        );
    }

    private async Task HandleSubscriptionGiftsAsync(string rawBody, CancellationToken ct)
    {
        KickSubscriptionGiftsPayload? payload = Parse<KickSubscriptionGiftsPayload>(
            rawBody,
            "channel.subscription.gifts"
        );
        if (payload?.Broadcaster?.UserId is not { } broadcasterKickId)
        {
            WarnMissingIdentity(payload, "channel.subscription.gifts");
            return;
        }

        Guid tenantId = await ResolveTenantIdAsync(broadcasterKickId, ct);
        if (tenantId == Guid.Empty)
            return;

        // Anonymous gifter → empty identity + flag, the same convention as the Twitch translator.
        KickUserRef? gifter = payload.Gifter;
        bool isAnonymous = gifter?.IsAnonymous == true || gifter?.UserId is null;

        await _bus.PublishAsync(
            new GiftSubscriptionEvent
            {
                BroadcasterId = tenantId,
                OccurredAt = payload.CreatedAt ?? _clock.GetUtcNow(),
                GifterUserId = gifter?.UserId is { } gifterId ? KickId(gifterId) : string.Empty,
                GifterDisplayName = gifter?.Username ?? string.Empty,
                Tier = KickBaseTier,
                GiftCount = payload.Giftees?.Count ?? 0,
                IsAnonymous = isAnonymous,
                // Kick enumerates the recipients on the event itself (Twitch cannot) — carry them.
                Recipients =
                [
                    .. (payload.Giftees ?? [])
                        .Where(g => g.UserId is not null)
                        .Select(g => new GiftRecipient(
                            KickId(g.UserId!.Value),
                            g.Username ?? string.Empty
                        )),
                ],
            },
            ct
        );
    }

    // ─── channel.reward.redemption.updated → canonical redemption update ─────

    private async Task HandleRewardRedemptionAsync(string rawBody, CancellationToken ct)
    {
        KickRewardRedemptionPayload? payload = Parse<KickRewardRedemptionPayload>(
            rawBody,
            "channel.reward.redemption.updated"
        );
        if (
            payload?.Broadcaster?.UserId is not { } broadcasterKickId
            || payload.Id is not { Length: > 0 } redemptionId
            || payload.Redeemer?.UserId is not { } redeemerKickId
        )
        {
            WarnMissingIdentity(payload, "channel.reward.redemption.updated");
            return;
        }

        // Kick's statuses are pending/accepted/rejected; the canonical event models the COMPLETED
        // transition (fulfilled/canceled) — a still-pending update is the queued state, not a change
        // anyone alerts on.
        string? status = payload.Status?.ToLowerInvariant() switch
        {
            "accepted" => "fulfilled",
            "rejected" => "canceled",
            _ => null,
        };
        if (status is null)
            return;

        Guid tenantId = await ResolveTenantIdAsync(broadcasterKickId, ct);
        if (tenantId == Guid.Empty)
            return;

        await _bus.PublishAsync(
            new RewardRedemptionUpdatedEvent
            {
                BroadcasterId = tenantId,
                OccurredAt = payload.RedeemedAt ?? _clock.GetUtcNow(),
                RedemptionId = redemptionId,
                RewardId = payload.Reward?.Id ?? string.Empty,
                RewardTitle = payload.Reward?.Title ?? string.Empty,
                UserId = KickId(redeemerKickId),
                UserDisplayName = payload.Redeemer.Username ?? string.Empty,
                Status = status,
            },
            ct
        );
    }

    // ─── moderation.banned → canonical ban / timeout ─────────────────────────

    private async Task HandleModerationBannedAsync(string rawBody, CancellationToken ct)
    {
        KickModerationBannedPayload? payload = Parse<KickModerationBannedPayload>(
            rawBody,
            "moderation.banned"
        );
        if (
            payload?.Broadcaster?.UserId is not { } broadcasterKickId
            || payload.BannedUser?.UserId is not { } bannedKickId
        )
        {
            WarnMissingIdentity(payload, "moderation.banned");
            return;
        }

        Guid tenantId = await ResolveTenantIdAsync(broadcasterKickId, ct);
        if (tenantId == Guid.Empty)
            return;

        DateTimeOffset occurredAt = payload.Metadata?.CreatedAt ?? _clock.GetUtcNow();
        string targetUserId = KickId(bannedKickId);
        string targetDisplayName = payload.BannedUser.Username ?? string.Empty;
        string moderatorUserId = payload.Moderator?.UserId is { } modId
            ? KickId(modId)
            : string.Empty;

        // expires_at distinguishes the two canonical shapes: null = permanent ban, set = timeout.
        if (payload.Metadata?.ExpiresAt is { } expiresAt)
        {
            int durationSeconds = (int)Math.Max(0, (expiresAt - occurredAt).TotalSeconds);
            await _bus.PublishAsync(
                new UserTimedOutEvent
                {
                    BroadcasterId = tenantId,
                    OccurredAt = occurredAt,
                    TargetUserId = targetUserId,
                    TargetDisplayName = targetDisplayName,
                    ModeratorUserId = moderatorUserId,
                    DurationSeconds = durationSeconds,
                    Reason = payload.Metadata?.Reason,
                },
                ct
            );
            return;
        }

        await _bus.PublishAsync(
            new UserBannedEvent
            {
                BroadcasterId = tenantId,
                OccurredAt = occurredAt,
                TargetUserId = targetUserId,
                TargetDisplayName = targetDisplayName,
                ModeratorUserId = moderatorUserId,
                Reason = payload.Metadata?.Reason,
            },
            ct
        );
    }

    // ─── kicks.gifted → canonical CheerEvent (the bits analog) ───────────────

    private async Task HandleKicksGiftedAsync(string rawBody, CancellationToken ct)
    {
        KickKicksGiftedPayload? payload = Parse<KickKicksGiftedPayload>(rawBody, "kicks.gifted");
        if (
            payload?.Broadcaster?.UserId is not { } broadcasterKickId
            || payload.Sender?.UserId is not { } senderKickId
            || payload.Gift?.Amount is not { } amount
        )
        {
            WarnMissingIdentity(payload, "kicks.gifted");
            return;
        }

        Guid tenantId = await ResolveTenantIdAsync(broadcasterKickId, ct);
        if (tenantId == Guid.Empty)
            return;

        // Kicks are Kick's paid on-platform currency — the exact role bits play on Twitch, so they
        // ride the canonical cheer (alerts, engagement earning, dashboard push) with Bits = amount.
        await _bus.PublishAsync(
            new CheerEvent
            {
                BroadcasterId = tenantId,
                OccurredAt = payload.CreatedAt ?? _clock.GetUtcNow(),
                UserId = KickId(senderKickId),
                UserDisplayName = payload.Sender.Username ?? string.Empty,
                Bits = amount,
                Message = payload.Gift.Message ?? string.Empty,
                IsAnonymous = payload.Sender.IsAnonymous == true,
            },
            ct
        );
    }

    // ─── shared plumbing ─────────────────────────────────────────────────────

    private TPayload? Parse<TPayload>(string rawBody, string eventType)
        where TPayload : class
    {
        try
        {
            return JsonSerializer.Deserialize<TPayload>(rawBody);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Unparseable Kick {EventType} payload — skipping", eventType);
            return null;
        }
    }

    private void WarnMissingIdentity(object? payload, string eventType)
    {
        // A parse failure already logged; only a parsed-but-incomplete payload warrants the warn.
        if (payload is not null)
            _logger.LogWarning(
                "Kick {EventType} payload missing required identity — skipping",
                eventType
            );
    }

    private async Task<Guid> ResolveTenantIdAsync(long broadcasterKickId, CancellationToken ct)
    {
        string externalChannelId = KickId(broadcasterKickId);
        Guid tenantId = await _db
            .Channels.Where(c =>
                c.Provider == AuthEnums.Platform.Kick && c.ExternalChannelId == externalChannelId
            )
            .Select(c => c.Id)
            .FirstOrDefaultAsync(ct);
        if (tenantId == Guid.Empty)
            _logger.LogDebug(
                "Kick event for unknown broadcaster {KickId} — skipping",
                broadcasterKickId
            );
        return tenantId;
    }

    private async Task<Channel?> ResolveTenantAsync(long broadcasterKickId, CancellationToken ct)
    {
        string externalChannelId = KickId(broadcasterKickId);
        Channel? tenant = await _db.Channels.FirstOrDefaultAsync(
            c => c.Provider == AuthEnums.Platform.Kick && c.ExternalChannelId == externalChannelId,
            ct
        );
        if (tenant is null)
            _logger.LogDebug(
                "Kick event for unknown broadcaster {KickId} — skipping",
                broadcasterKickId
            );
        return tenant;
    }

    private static string KickId(long id) => id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Kick's channel slug is the stable lowercase handle; fall back to the username.</summary>
    private static string Login(KickUserRef user) =>
        user.ChannelSlug ?? user.Username?.ToLowerInvariant() ?? string.Empty;
}
