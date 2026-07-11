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
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Chat.Kick;

/// <summary>
/// <see cref="IKickWebhookIngest"/>: the Kick chat READ leg (slice 3b-2c-2). Resolves the tenant by the
/// broadcaster's numeric Kick id against the kick-provider <c>Channel</c> row (provisioned by the
/// reconcile worker when the streamer connects — a webhook for an unknown broadcaster is skipped, the
/// same posture as EventSub's unknown-channel guard), dedupes redeliveries against the persisted
/// <c>ChatMessages</c>, and publishes the canonical <see cref="ChatMessageReceivedEvent"/>. Role flags
/// map from Kick's badge types; message text passes through as one raw fragment (Kick's emote
/// placeholders stay inline — the same plain-text posture as the YouTube ingest).
/// </summary>
public sealed class KickWebhookIngest : IKickWebhookIngest
{
    private readonly IApplicationDbContext _db;
    private readonly IEventBus _bus;
    private readonly TimeProvider _clock;
    private readonly ILogger<KickWebhookIngest> _logger;

    public KickWebhookIngest(
        IApplicationDbContext db,
        IEventBus bus,
        TimeProvider clock,
        ILogger<KickWebhookIngest> logger
    )
    {
        _db = db;
        _bus = bus;
        _clock = clock;
        _logger = logger;
    }

    public async Task HandleChatMessageAsync(
        string rawBody,
        CancellationToken cancellationToken = default
    )
    {
        ChatMessagePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ChatMessagePayload>(rawBody);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Unparseable Kick chat.message.sent payload — skipping");
            return;
        }

        if (
            payload?.MessageId is not { Length: > 0 } messageId
            || payload.Broadcaster?.UserId is not { } broadcasterKickId
            || payload.Sender?.UserId is not { } senderKickId
        )
        {
            _logger.LogWarning("Kick chat.message.sent payload missing identity — skipping");
            return;
        }

        string externalChannelId = broadcasterKickId.ToString(CultureInfo.InvariantCulture);
        Guid tenantId = await _db
            .Channels.Where(c =>
                c.Provider == AuthEnums.Platform.Kick && c.ExternalChannelId == externalChannelId
            )
            .Select(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (tenantId == Guid.Empty)
        {
            _logger.LogDebug(
                "Kick chat message for unknown broadcaster {KickId} — skipping",
                broadcasterKickId
            );
            return;
        }

        // Kick retries undelivered webhooks — anything already persisted has already been broadcast.
        bool seen = await _db.ChatMessages.AnyAsync(m => m.Id == messageId, cancellationToken);
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
                TwitchBroadcasterId = externalChannelId,
                UserId = senderKickId.ToString(CultureInfo.InvariantCulture),
                UserDisplayName = payload.Sender.Username ?? string.Empty,
                // Kick's channel slug is the stable lowercase handle; fall back to the username.
                UserLogin =
                    payload.Sender.ChannelSlug
                    ?? payload.Sender.Username?.ToLowerInvariant()
                    ?? string.Empty,
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
            cancellationToken
        );
    }

    // ─── Wire model (chat.message.sent v1, verified against live docs 2026-07-11) ───

    private sealed class ChatMessagePayload
    {
        [JsonPropertyName("message_id")]
        public string? MessageId { get; set; }

        [JsonPropertyName("broadcaster")]
        public KickUserRef? Broadcaster { get; set; }

        [JsonPropertyName("sender")]
        public KickUserRef? Sender { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }
    }

    private sealed class KickUserRef
    {
        [JsonPropertyName("user_id")]
        public long? UserId { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("channel_slug")]
        public string? ChannelSlug { get; set; }

        [JsonPropertyName("identity")]
        public KickIdentity? Identity { get; set; }
    }

    private sealed class KickIdentity
    {
        [JsonPropertyName("badges")]
        public List<KickBadge>? Badges { get; set; }
    }

    private sealed class KickBadge
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }
}
