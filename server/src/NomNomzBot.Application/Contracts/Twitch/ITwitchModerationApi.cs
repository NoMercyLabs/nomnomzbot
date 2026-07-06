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

namespace NomNomzBot.Application.Contracts.Twitch;

/// <summary>
/// The Helix "Moderation" category sub-client: chat enforcement for a channel — bans / timeouts, unban
/// requests, the blocked-term list, chat-message deletion, Shield Mode, warnings, suspicious-user status,
/// and AutoMod (status checks, held-message review, and settings) (twitch-helix.md §3.2). One of the grouped
/// sub-clients exposed by <see cref="ITwitchHelixClient"/>. Every method takes the owning tenant as a
/// <see cref="Guid"/> and resolves it to the Twitch id internally (the invariant: a Guid never reaches
/// Twitch); target / other users are passed as raw Twitch id strings. Each returns
/// <see cref="Result"/>/<see cref="Result{T}"/> carrying a closed <see cref="TwitchErrorCodes"/> on failure.
/// All endpoints use the broadcaster's user token. Many require both <c>broadcaster_id</c> and
/// <c>moderator_id</c>; the tenant moderates their own channel, so the resolved Twitch id is sent for both.
/// </summary>
public interface ITwitchModerationApi
{
    /// <summary>Ban User — permanently bans <paramref name="targetTwitchUserId"/> from the channel's chat. Requires <c>moderator:manage:banned_users</c>.</summary>
    Task<Result<TwitchBanResult>> BanUserAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        string? reason,
        CancellationToken ct = default
    );

    /// <summary>
    /// Ban User AS THE OPERATOR — bans <paramref name="targetTwitchUserId"/> from
    /// <paramref name="broadcasterTwitchId"/>'s chat using the logged-in operator's OWN token
    /// (<c>moderator_id</c> = the operator), so it works in ANY channel Twitch has made the operator a moderator
    /// of — tenant or not (chat-client.md §3.5). <paramref name="broadcasterTwitchId"/> is a raw Twitch id, never
    /// a tenant Guid. Requires the operator token to carry <c>moderator:manage:banned_users</c>; Twitch enforces
    /// that the operator actually moderates the channel, so there is no privilege escalation.
    /// </summary>
    Task<Result<TwitchBanResult>> BanAsOperatorAsync(
        Guid operatorUserId,
        string broadcasterTwitchId,
        string targetTwitchUserId,
        string? reason,
        CancellationToken ct = default
    );

    /// <summary>Timeout User — times out <paramref name="targetTwitchUserId"/> for <paramref name="durationSeconds"/>. Requires <c>moderator:manage:banned_users</c>.</summary>
    Task<Result<TwitchBanResult>> TimeoutUserAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        int durationSeconds,
        string? reason,
        CancellationToken ct = default
    );

    /// <summary>Unban User — lifts the ban or timeout on <paramref name="targetTwitchUserId"/>. Requires <c>moderator:manage:banned_users</c>.</summary>
    Task<Result> UnbanUserAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        CancellationToken ct = default
    );

    /// <summary>Get Banned Users — one page of the channel's banned / timed-out users. Requires <c>moderation:read</c>.</summary>
    Task<Result<TwitchPage<TwitchBannedUser>>> GetBannedUsersAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>Get Unban Requests — one page of unban requests filtered by <paramref name="status"/> (pending / approved / denied / …). Requires <c>moderator:read:unban_requests</c>.</summary>
    Task<Result<TwitchPage<TwitchUnbanRequest>>> GetUnbanRequestsAsync(
        Guid broadcasterId,
        string status,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>Resolve Unban Request — approves or denies <paramref name="unbanRequestId"/>, optionally with resolution text. Requires <c>moderator:manage:unban_requests</c>.</summary>
    Task<Result<TwitchUnbanRequest>> ResolveUnbanRequestAsync(
        Guid broadcasterId,
        string unbanRequestId,
        string status,
        string? resolutionText,
        CancellationToken ct = default
    );

    /// <summary>Get Blocked Terms — one page of the channel's blocked words / phrases. Requires <c>moderator:read:blocked_terms</c>.</summary>
    Task<Result<TwitchPage<TwitchBlockedTerm>>> GetBlockedTermsAsync(
        Guid broadcasterId,
        TwitchPageRequest page,
        CancellationToken ct = default
    );

    /// <summary>Add Blocked Term — adds <paramref name="text"/> to the block list and returns the created term. Requires <c>moderator:manage:blocked_terms</c>.</summary>
    Task<Result<TwitchBlockedTerm>> AddBlockedTermAsync(
        Guid broadcasterId,
        string text,
        CancellationToken ct = default
    );

    /// <summary>Remove Blocked Term — removes the term identified by <paramref name="blockedTermId"/>. Requires <c>moderator:manage:blocked_terms</c>.</summary>
    Task<Result> RemoveBlockedTermAsync(
        Guid broadcasterId,
        string blockedTermId,
        CancellationToken ct = default
    );

    /// <summary>Delete Chat Message — removes the single message identified by <paramref name="messageId"/>. Requires <c>moderator:manage:chat_messages</c>.</summary>
    Task<Result> DeleteChatMessageAsync(
        Guid broadcasterId,
        string messageId,
        CancellationToken ct = default
    );

    /// <summary>Clear Chat — removes every message from the channel's chat room (omits <c>message_id</c>). Requires <c>moderator:manage:chat_messages</c>.</summary>
    Task<Result> DeleteAllChatMessagesAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>Get Shield Mode Status — the channel's current Shield Mode activation status. Requires <c>moderator:read:shield_mode</c>.</summary>
    Task<Result<TwitchShieldModeStatus>> GetShieldModeStatusAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Update Shield Mode Status — activates or deactivates Shield Mode and returns the new status. Requires <c>moderator:manage:shield_mode</c>.</summary>
    Task<Result<TwitchShieldModeStatus>> UpdateShieldModeStatusAsync(
        Guid broadcasterId,
        bool isActive,
        CancellationToken ct = default
    );

    /// <summary>Warn Chat User — warns <paramref name="targetTwitchUserId"/>, gating their chat until they acknowledge it. Requires <c>moderator:manage:warnings</c>.</summary>
    Task<Result<TwitchWarningResult>> WarnChatUserAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        string reason,
        CancellationToken ct = default
    );

    /// <summary>Add Suspicious Status — flags <paramref name="targetTwitchUserId"/> as <c>ACTIVE_MONITORING</c> or <c>RESTRICTED</c>. Requires <c>moderator:manage:suspicious_users</c>.</summary>
    Task<Result<TwitchSuspiciousUserStatus>> AddSuspiciousStatusAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        string status,
        CancellationToken ct = default
    );

    /// <summary>Remove Suspicious Status — clears the suspicious-user flag from <paramref name="targetTwitchUserId"/>. Requires <c>moderator:manage:suspicious_users</c>.</summary>
    Task<Result<TwitchSuspiciousUserStatus>> RemoveSuspiciousStatusAsync(
        Guid broadcasterId,
        string targetTwitchUserId,
        CancellationToken ct = default
    );

    /// <summary>Check AutoMod Status — tests whether each given message would be held by AutoMod. Requires <c>moderation:read</c>.</summary>
    Task<Result<IReadOnlyList<TwitchAutoModStatus>>> CheckAutoModStatusAsync(
        Guid broadcasterId,
        IReadOnlyList<(string MsgId, string MsgText)> messages,
        CancellationToken ct = default
    );

    /// <summary>Manage Held AutoMod Message — allows (<c>approve</c>) or denies the AutoMod-held message <paramref name="messageId"/>. Requires <c>moderator:manage:automod</c>.</summary>
    Task<Result> ManageHeldAutoModMessageAsync(
        Guid broadcasterId,
        string messageId,
        bool approve,
        CancellationToken ct = default
    );

    /// <summary>Get AutoMod Settings — the channel's overall and per-category AutoMod levels. Requires <c>moderator:read:automod_settings</c>.</summary>
    Task<Result<TwitchAutoModSettings>> GetAutoModSettingsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Update AutoMod Settings — sets the overall level or the per-category levels and returns the applied settings. Requires <c>moderator:manage:automod_settings</c>.</summary>
    Task<Result<TwitchAutoModSettings>> UpdateAutoModSettingsAsync(
        Guid broadcasterId,
        UpdateAutoModSettingsRequest settings,
        CancellationToken ct = default
    );
}
