// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace NomNomzBot.Infrastructure.Chat.Kick;

// Wire models for Kick's webhook payloads (all v1), verified against live docs.kick.com
// (chat 2026-07-11; livestream/community/monetization 2026-07-16). Every field is nullable —
// the ingest validates what each handler actually needs and skips deliveries missing it.

/// <summary>The user envelope Kick embeds on every event (broadcaster / sender / follower / …).</summary>
internal sealed class KickUserRef
{
    [JsonPropertyName("is_anonymous")]
    public bool? IsAnonymous { get; set; }

    [JsonPropertyName("user_id")]
    public long? UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("channel_slug")]
    public string? ChannelSlug { get; set; }

    [JsonPropertyName("identity")]
    public KickIdentity? Identity { get; set; }
}

internal sealed class KickIdentity
{
    [JsonPropertyName("badges")]
    public List<KickBadge>? Badges { get; set; }
}

internal sealed class KickBadge
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

/// <summary><c>chat.message.sent</c>.</summary>
internal sealed class KickChatMessagePayload
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

/// <summary><c>livestream.status.updated</c>.</summary>
internal sealed class KickLivestreamStatusPayload
{
    [JsonPropertyName("broadcaster")]
    public KickUserRef? Broadcaster { get; set; }

    [JsonPropertyName("is_live")]
    public bool? IsLive { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

/// <summary><c>livestream.metadata.updated</c>.</summary>
internal sealed class KickLivestreamMetadataPayload
{
    [JsonPropertyName("broadcaster")]
    public KickUserRef? Broadcaster { get; set; }

    [JsonPropertyName("metadata")]
    public KickLivestreamMetadata? Metadata { get; set; }
}

internal sealed class KickLivestreamMetadata
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("category")]
    public KickCategory? Category { get; set; }
}

internal sealed class KickCategory
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary><c>channel.followed</c>.</summary>
internal sealed class KickFollowedPayload
{
    [JsonPropertyName("broadcaster")]
    public KickUserRef? Broadcaster { get; set; }

    [JsonPropertyName("follower")]
    public KickUserRef? Follower { get; set; }
}

/// <summary><c>channel.subscription.new</c> and <c>channel.subscription.renewal</c> (same shape).</summary>
internal sealed class KickSubscriptionPayload
{
    [JsonPropertyName("broadcaster")]
    public KickUserRef? Broadcaster { get; set; }

    [JsonPropertyName("subscriber")]
    public KickUserRef? Subscriber { get; set; }

    /// <summary>Months subscribed.</summary>
    [JsonPropertyName("duration")]
    public int? Duration { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary><c>channel.subscription.gifts</c>.</summary>
internal sealed class KickSubscriptionGiftsPayload
{
    [JsonPropertyName("broadcaster")]
    public KickUserRef? Broadcaster { get; set; }

    /// <summary>Fields are null when <c>is_anonymous</c> is true.</summary>
    [JsonPropertyName("gifter")]
    public KickUserRef? Gifter { get; set; }

    [JsonPropertyName("giftees")]
    public List<KickUserRef>? Giftees { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
}

/// <summary><c>channel.reward.redemption.updated</c>.</summary>
internal sealed class KickRewardRedemptionPayload
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("user_input")]
    public string? UserInput { get; set; }

    /// <summary><c>pending</c> | <c>accepted</c> | <c>rejected</c>.</summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("redeemed_at")]
    public DateTimeOffset? RedeemedAt { get; set; }

    [JsonPropertyName("reward")]
    public KickReward? Reward { get; set; }

    [JsonPropertyName("redeemer")]
    public KickUserRef? Redeemer { get; set; }

    [JsonPropertyName("broadcaster")]
    public KickUserRef? Broadcaster { get; set; }
}

internal sealed class KickReward
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }
}

/// <summary><c>moderation.banned</c>.</summary>
internal sealed class KickModerationBannedPayload
{
    [JsonPropertyName("broadcaster")]
    public KickUserRef? Broadcaster { get; set; }

    [JsonPropertyName("moderator")]
    public KickUserRef? Moderator { get; set; }

    [JsonPropertyName("banned_user")]
    public KickUserRef? BannedUser { get; set; }

    [JsonPropertyName("metadata")]
    public KickBanMetadata? Metadata { get; set; }
}

internal sealed class KickBanMetadata
{
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Null for a permanent ban; set for a timeout.</summary>
    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary><c>kicks.gifted</c> — Kick's paid-currency gift (the bits analog).</summary>
internal sealed class KickKicksGiftedPayload
{
    [JsonPropertyName("broadcaster")]
    public KickUserRef? Broadcaster { get; set; }

    [JsonPropertyName("sender")]
    public KickUserRef? Sender { get; set; }

    [JsonPropertyName("gift")]
    public KickGift? Gift { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
}

internal sealed class KickGift
{
    [JsonPropertyName("amount")]
    public int? Amount { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
