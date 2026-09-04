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
    private readonly NomNomzBot.Application.Contracts.Chat.IBotSelfEchoGuard _selfEchoGuard;

    public KickWebhookIngest(
        IApplicationDbContext db,
        IEventBus bus,
        IChannelRegistry registry,
        TimeProvider clock,
        ILogger<KickWebhookIngest> logger,
        NomNomzBot.Application.Contracts.Chat.IBotSelfEchoGuard selfEchoGuard
    )
    {
        _db = db;
        _bus = bus;
        _registry = registry;
        _clock = clock;
        _logger = logger;
        _selfEchoGuard = selfEchoGuard;
    }

    /// <summary>Idempotency scope for non-chat Kick webhook redeliveries (follows/subs/gifts/bans/redemptions).
    /// Chat already dedupes against the persisted <c>ChatMessages</c> row, so it is excluded here.</summary>
    private const string RedeliveryScope = "kick-webhook";

    /// <summary>How long a redelivery marker is kept before retention may prune it — comfortably longer
    /// than any plausible Kick retry window.</summary>
    private static readonly TimeSpan RedeliveryRetention = TimeSpan.FromDays(7);

    public async Task HandleAsync(
        string eventType,
        string rawBody,
        string messageId = "",
        CancellationToken cancellationToken = default
    )
    {
        // Chat owns its own dedupe (against the persisted ChatMessages row); every other event type is
        // guarded here by the Kick-Event-Message-Id header so a redelivered follow/sub/gift/ban/redemption
        // is processed at most once instead of double-crediting or double-firing its alert.
        bool guardRedelivery = eventType != "chat.message.sent" && messageId.Length > 0;
        if (guardRedelivery && !await TryClaimRedeliveryAsync(messageId, cancellationToken))
            return;

        await DispatchAsync(eventType, rawBody, cancellationToken);
    }

    /// <summary>
    /// Claims a Kick delivery id BEFORE dispatching it, with the same atomic insert-and-catch the EventSub
    /// suppressor uses (<see cref="Application.Contracts.Twitch.IDuplicateNotificationSuppressor"/>). The
    /// previous shape read <c>AnyAsync</c>, dispatched, and only then inserted the key — so two concurrent
    /// deliveries of one redelivered event both passed the read and both fanned out, and the write that was
    /// meant to stop the second happened after the damage. Kick redelivers on its own schedule, so this does
    /// not need a deploy overlap to fire. Claiming first makes the unique constraint the arbiter: exactly one
    /// caller wins, and the loser's <see cref="DbUpdateException"/> IS the "already processed" answer.
    /// <para>
    /// Kept as its own claim rather than routed through the EventSub suppressor: that interface is keyed on
    /// (broadcaster, subscription type, raw payload) because Twitch gives no reliable per-delivery id, while
    /// Kick hands us an explicit <c>Kick-Event-Message-Id</c> header, which is a better key than a payload
    /// hash. Two call sites sharing a pattern is not yet cause to unify them behind an interface that fits
    /// neither.
    /// </para>
    /// </summary>
    /// <summary>
    /// The "not tenant-scoped" broadcaster value for a Kick delivery claim. A Kick <c>Kick-Event-Message-Id</c>
    /// is unique across the platform, so the claim needs no tenant — but it cannot be NULL, because the unique
    /// index counts NULLs as distinct and would admit every duplicate.
    /// </summary>
    private static readonly Guid NotTenantScoped = Guid.Empty;

    private async Task<bool> TryClaimRedeliveryAsync(
        string messageId,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset now = _clock.GetUtcNow();

        // Release an expired claim for this exact key first, so a legitimate later delivery carrying a reused
        // id is not suppressed forever. Targeted at the unique key, not a scan.
        DateTime nowUtc = now.UtcDateTime;
        await _db
            .IdempotencyKeys.Where(k =>
                k.Scope == RedeliveryScope
                && k.Key == messageId
                && k.BroadcasterId == NotTenantScoped
                && k.ExpiresAt <= nowUtc
            )
            .ExecuteDeleteAsync(cancellationToken);

        _db.IdempotencyKeys.Add(
            new Domain.Platform.Entities.IdempotencyKey
            {
                Scope = RedeliveryScope,
                Key = messageId,
                // MUST be non-null. The unique index is (Scope, Key, BroadcasterId), and SQL treats NULLs as
                // DISTINCT in a unique index — so leaving this null lets every claim insert succeed and the
                // constraint silently stops arbitrating. A Kick delivery id is globally unique already, so it
                // is not tenant-scoped; Guid.Empty is the explicit "no tenant" value that still collides.
                BroadcasterId = NotTenantScoped,
                CreatedAt = nowUtc,
                ExpiresAt = now.Add(RedeliveryRetention).UtcDateTime,
            }
        );

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex)
        {
            // Lost the race — another process, or an earlier delivery still inside the retention window,
            // already holds this claim. Detach so the failed insert does not linger in the change tracker.
            foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry in ex.Entries)
                entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            return false;
        }
    }

    private Task DispatchAsync(
        string eventType,
        string rawBody,
        CancellationToken cancellationToken
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

        // S009 — a line the bot itself typed (a dedicated bot account, or a marked line on the self-host
        // owner account) must never re-enter as a fresh command trigger.
        if (
            await _selfEchoGuard.ShouldSuppressAsync(
                tenantId,
                AuthEnums.Platform.Kick,
                senderKickIdText,
                payload.Content ?? string.Empty,
                ct
            )
        )
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
                Fragments = BuildFragments(payload.Content ?? string.Empty, payload.Emotes),
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

        // Idempotent under Kick's redeliveries: only a genuine transition re-publishes the canonical
        // event — a repeated "still live"/"still offline" delivery must never re-fire go-live alerts,
        // Discord notifications, or a second stream session.
        bool wasLive = tenant.IsLive;
        tenant.IsLive = isLive;
        if (!string.IsNullOrWhiteSpace(payload.Title))
            tenant.Title = payload.Title;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Kick tenant {TenantId} is now {State}",
            tenant.Id,
            isLive ? "LIVE" : "OFFLINE"
        );

        if (isLive == wasLive)
            return;

        // The SAME canonical stream-lifecycle events Twitch's StreamLifecycleTranslators publish, so
        // alerts, Discord go-live, and stream-session creation fire identically for a Kick-only tenant —
        // no Kick-specific consumer needed anywhere downstream.
        if (isLive)
        {
            await _bus.PublishAsync(
                new ChannelOnlineEvent
                {
                    Provider = AuthEnums.Platform.Kick,
                    BroadcasterId = tenant.Id,
                    OccurredAt = _clock.GetUtcNow(),
                    BroadcasterDisplayName = payload.Broadcaster.Username ?? string.Empty,
                    StreamTitle = tenant.Title ?? string.Empty,
                    GameName = tenant.GameName ?? string.Empty,
                    StartedAt = _clock.GetUtcNow(),
                },
                ct
            );
        }
        else
        {
            await _bus.PublishAsync(
                new ChannelOfflineEvent
                {
                    Provider = AuthEnums.Platform.Kick,
                    BroadcasterId = tenant.Id,
                    OccurredAt = _clock.GetUtcNow(),
                    BroadcasterDisplayName = payload.Broadcaster.Username ?? string.Empty,
                    // Kick's livestream.status.updated carries no duration — degrades to zero exactly
                    // like Twitch's stream.offline translator; elapsed uptime is computed downstream.
                    StreamDuration = TimeSpan.Zero,
                },
                ct
            );
        }
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
                Provider = AuthEnums.Platform.Kick,
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
                Provider = AuthEnums.Platform.Kick,
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
                Provider = AuthEnums.Platform.Kick,
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
                Provider = AuthEnums.Platform.Kick,
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
                Provider = AuthEnums.Platform.Kick,
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
                Provider = AuthEnums.Platform.Kick,
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
                    Provider = AuthEnums.Platform.Kick,
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
                Provider = AuthEnums.Platform.Kick,
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
                Provider = AuthEnums.Platform.Kick,
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

    /// <summary>
    /// Splits a Kick chat message's flat <c>content</c> into text/emote fragments — the same shape
    /// Twitch's <c>fragments[]</c> array carries — using the <c>emotes[]</c> character spans Kick's
    /// <c>chat.message.sent</c> payload provides alongside the inline <c>[emote:ID:NAME]</c> placeholder
    /// text. No emotes (or an empty content) degrades to a single text fragment, same as before this
    /// existed. Overlapping/out-of-range spans from a malformed delivery are skipped rather than faulted.
    /// </summary>
    private static List<ChatMessageFragment> BuildFragments(
        string content,
        List<KickEmoteEntry>? emotes
    )
    {
        List<(int Start, int End, string EmoteId)> spans =
        [
            .. (emotes ?? [])
                .Where(e => e.EmoteId is { Length: > 0 })
                .SelectMany(e =>
                    (e.Positions ?? []).Select(p => (Start: p.Start, End: p.End, e.EmoteId!))
                )
                .Where(s =>
                    s.Start is >= 0 && s.End is >= 0 && s.End >= s.Start && s.End < content.Length
                )
                .Select(s => (s.Start!.Value, s.End!.Value, s.Item3))
                .OrderBy(s => s.Item1),
        ];

        if (spans.Count == 0)
            return [new() { Type = "text", Text = content }];

        List<ChatMessageFragment> fragments = [];
        int cursor = 0;
        foreach ((int start, int endInclusive, string emoteId) in spans)
        {
            // A malformed/overlapping span (starts before the previous one ended) is skipped rather
            // than faulted — the delivery still carries the flat Message for a degraded rendering.
            if (start < cursor)
                continue;

            if (start > cursor)
                fragments.Add(new() { Type = "text", Text = content[cursor..start] });

            // Kick's positions are BOTH ends inclusive — end+1 is the exclusive slice bound.
            int end = endInclusive + 1;
            fragments.Add(
                new()
                {
                    Type = "emote",
                    Text = content[start..end],
                    EmoteId = emoteId,
                }
            );
            cursor = end;
        }

        if (cursor < content.Length)
            fragments.Add(new() { Type = "text", Text = content[cursor..] });

        return fragments;
    }

    private static string KickId(long id) => id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Kick's channel slug is the stable lowercase handle; fall back to the username.</summary>
    private static string Login(KickUserRef user) =>
        user.ChannelSlug ?? user.Username?.ToLowerInvariant() ?? string.Empty;
}
