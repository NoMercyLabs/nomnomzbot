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
/// The Helix "Guest Star" category sub-client (BETA): channel settings, the live session and its guests,
/// pending invites, and slot management (twitch-helix.md §3.2). One of the grouped sub-clients exposed by
/// <see cref="ITwitchHelixClient"/>. Every method takes the owning tenant as a <see cref="Guid"/> and
/// resolves it to the Twitch id internally (the invariant: a Guid never reaches Twitch). Guests and target
/// users are raw Twitch id strings. Each returns <see cref="Result"/>/<see cref="Result{T}"/> carrying a
/// closed <see cref="TwitchErrorCodes"/> on failure.
///
/// Every read carrying <c>moderator_id</c> and every slot/invite mutation requires both
/// <c>broadcaster_id</c> and <c>moderator_id</c>. The tenant acts on their own channel with their own
/// token, so the single resolved Twitch id is sent for both — the same convention as the moderation
/// sub-client.
/// </summary>
public interface ITwitchGuestStarApi
{
    /// <summary>Get Channel Guest Star Settings — the host's Guest Star configuration. Requires <c>channel:read:guest_star</c>.</summary>
    Task<Result<TwitchGuestStarChannelSettings>> GetChannelSettingsAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Update Channel Guest Star Settings — mutate the host's Guest Star configuration. Status-only. Requires <c>channel:manage:guest_star</c>.</summary>
    Task<Result> UpdateChannelSettingsAsync(
        Guid broadcasterId,
        UpdateGuestStarSettingsRequest request,
        CancellationToken ct = default
    );

    /// <summary>Get Guest Star Session — the ongoing session and the guests in its slots. Requires <c>channel:read:guest_star</c>.</summary>
    Task<Result<TwitchGuestStarSession>> GetSessionAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>Create Guest Star Session — start a session on behalf of the broadcaster, returning the created session. Requires <c>channel:manage:guest_star</c>.</summary>
    Task<Result<TwitchGuestStarSession>> CreateSessionAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    );

    /// <summary>End Guest Star Session — end the session on behalf of the broadcaster, returning the ended session. Requires <c>channel:manage:guest_star</c>.</summary>
    Task<Result<TwitchGuestStarSession>> EndSessionAsync(
        Guid broadcasterId,
        string sessionId,
        CancellationToken ct = default
    );

    /// <summary>Get Guest Star Invites — the pending invites to a session, with each invitee's ready status. Requires <c>channel:read:guest_star</c>.</summary>
    Task<Result<IReadOnlyList<TwitchGuestStarInvite>>> GetInvitesAsync(
        Guid broadcasterId,
        string sessionId,
        CancellationToken ct = default
    );

    /// <summary>Send Guest Star Invite — invite a guest to the in-progress session. Status-only. Requires <c>channel:manage:guest_star</c>.</summary>
    Task<Result> SendInviteAsync(
        Guid broadcasterId,
        string sessionId,
        string guestTwitchUserId,
        CancellationToken ct = default
    );

    /// <summary>Delete Guest Star Invite — revoke a previously sent invite. Status-only. Requires <c>channel:manage:guest_star</c>.</summary>
    Task<Result> DeleteInviteAsync(
        Guid broadcasterId,
        string sessionId,
        string guestTwitchUserId,
        CancellationToken ct = default
    );

    /// <summary>Assign Guest Star Slot — place a ready guest into a slot in the active session. Status-only. Requires <c>channel:manage:guest_star</c>.</summary>
    Task<Result> AssignSlotAsync(
        Guid broadcasterId,
        string sessionId,
        string guestTwitchUserId,
        string slotId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Update Guest Star Slot — move a guest from one slot to another (swapping if the destination is
    /// occupied). A null destination exchanges the guest with the empty interface. Status-only.
    /// Requires <c>channel:manage:guest_star</c>.
    /// </summary>
    Task<Result> UpdateSlotAsync(
        Guid broadcasterId,
        string sessionId,
        string sourceSlotId,
        string? destinationSlotId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Delete Guest Star Slot — remove a guest's slot assignment, revoking their access. Optionally
    /// re-invites them back to the waiting room. Status-only. Requires <c>channel:manage:guest_star</c>.
    /// </summary>
    Task<Result> DeleteSlotAsync(
        Guid broadcasterId,
        string sessionId,
        string guestTwitchUserId,
        string slotId,
        bool? shouldReinviteGuest,
        CancellationToken ct = default
    );

    /// <summary>
    /// Update Guest Star Slot Settings — toggle a guest's audio / video / live state or set their volume
    /// within the session. Only the supplied settings are sent. Status-only.
    /// Requires <c>channel:manage:guest_star</c>.
    /// </summary>
    Task<Result> UpdateSlotSettingsAsync(
        Guid broadcasterId,
        string sessionId,
        string slotId,
        bool? isAudioEnabled,
        bool? isVideoEnabled,
        bool? isLive,
        int? volume,
        CancellationToken ct = default
    );
}
