// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.Kick;

/// <summary>
/// Pure HTTP transport over Kick's public API (api.kick.com/public/v1) for the chat + moderation surface
/// (BUILD slice 3b-2c; wire facts verified against live docs.kick.com 2026-07-11). All calls ride the
/// STREAMER's own OAuth bearer; failures degrade to typed <see cref="Result"/> codes exactly like the
/// YouTube twin (401/403 → <c>MISSING_SCOPE</c>, 404 → <c>NOT_FOUND</c>, other → <c>SERVICE_UNAVAILABLE</c>)
/// so a missing scope surfaces as a reauth need, never a crash. Kick ids are numeric.
/// </summary>
public interface IKickApiClient
{
    /// <summary>
    /// Sends a chat message (<c>POST /public/v1/chat</c>, scope <c>chat:write</c>) as the token's own
    /// user into <paramref name="broadcasterUserId"/>'s channel. Kick caps a message at 500 characters —
    /// longer input is rejected <c>VALIDATION_FAILED</c> before any call. Kick supports NATIVE replies:
    /// pass <paramref name="replyToMessageId"/> to thread onto an existing message. Returns the created
    /// message id.
    /// </summary>
    Task<Result<string>> SendMessageAsync(
        string accessToken,
        long broadcasterUserId,
        string content,
        string? replyToMessageId = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Deletes a chat message (<c>DELETE /public/v1/chat/{id}</c>, scope
    /// <c>moderation:chat_message:manage</c>).</summary>
    Task<Result> DeleteMessageAsync(
        string accessToken,
        string messageId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Times a viewer out (<c>POST /public/v1/moderation/bans</c> WITH <c>duration</c>, scope
    /// <c>moderation:ban</c>). Kick durations are MINUTES 1–10080 — the caller passes minutes already
    /// clamped/converted. <paramref name="reason"/> is capped at 100 characters by Kick (truncated locally).
    /// </summary>
    Task<Result> TimeoutUserAsync(
        string accessToken,
        long broadcasterUserId,
        long userId,
        int durationMinutes,
        string? reason = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Permanently bans a viewer (<c>POST /public/v1/moderation/bans</c> WITHOUT a duration).</summary>
    Task<Result> BanUserAsync(
        string accessToken,
        long broadcasterUserId,
        long userId,
        string? reason = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Lifts a ban or timeout (<c>DELETE /public/v1/moderation/bans</c>). Kick unbans directly by
    /// (broadcaster, user) — no insert-returned ban id to bookkeep, unlike YouTube.
    /// </summary>
    Task<Result> UnbanUserAsync(
        string accessToken,
        long broadcasterUserId,
        long userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Lists the caller's event subscriptions (<c>GET /public/v1/events/subscriptions</c>, scope
    /// <c>events:subscribe</c>) — what the reconcile checks before creating.
    /// </summary>
    Task<Result<IReadOnlyList<KickEventSubscription>>> ListEventSubscriptionsAsync(
        string accessToken,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Subscribes the token owner's channel to the given events over the webhook transport
    /// (<c>POST /public/v1/events/subscriptions</c>; a USER token subscribes its own channel —
    /// <c>broadcaster_user_id</c> is ignored). The webhook callback URL itself is configured PER APP in
    /// the Kick developer dashboard, not per subscription. A per-event <c>error</c> in the response is
    /// surfaced as a failure.
    /// </summary>
    Task<Result> SubscribeAsync(
        string accessToken,
        IReadOnlyList<KickEventRequest> events,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Removes event subscriptions (<c>DELETE /public/v1/events/subscriptions</c>, scope
    /// <c>events:subscribe</c>) by id — called on disconnect so Kick stops delivering webhooks for a
    /// channel the streamer no longer has connected. A no-op (success) for an empty id list.
    /// </summary>
    Task<Result> UnsubscribeAsync(
        string accessToken,
        IReadOnlyList<string> subscriptionIds,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reads the broadcaster's channel (<c>GET /public/v1/channels?broadcaster_user_id=</c>, scope
    /// <c>channel:read</c>) — title, category, and the live viewer count when the channel is currently
    /// streaming. Used for the dashboard's title/category display and the viewer-count poll; Kick has no
    /// stream-tags concept.
    /// </summary>
    Task<Result<KickChannel>> GetChannelAsync(
        string accessToken,
        long broadcasterUserId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Applies a title/category change on the token owner's OWN channel (<c>PATCH /public/v1/channels</c>,
    /// scope <c>channel:write</c>). The token holder's channel is implicit — Kick's PATCH takes no
    /// <c>broadcaster_user_id</c>. A category NAME must already be resolved to Kick's numeric category id
    /// by the caller (Kick's channel PATCH takes an id, not a name).
    /// </summary>
    Task<Result> UpdateChannelAsync(
        string accessToken,
        string? streamTitle,
        int? categoryId,
        CancellationToken cancellationToken = default
    );
}

/// <summary>One channel's current title/category/live state (the GET read shape).</summary>
public sealed record KickChannel(
    long BroadcasterUserId,
    string? StreamTitle,
    string? CategoryName,
    int? CategoryId,
    bool IsLive,
    int ViewerCount
);

/// <summary>One registered Kick event subscription (the GET list item shape).</summary>
public sealed record KickEventSubscription(
    string Id,
    string Event,
    int Version,
    string Method,
    long BroadcasterUserId
);

/// <summary>One event to subscribe (the POST create item shape).</summary>
public sealed record KickEventRequest(string Name, int Version);
