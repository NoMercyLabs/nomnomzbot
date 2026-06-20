// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Twitch;

// Helix "Guest Star" category wire models (BETA — GET/PUT /guest_star/channel_settings,
// GET/POST/DELETE /guest_star/session, GET/POST/DELETE /guest_star/invites, POST/PATCH/DELETE
// /guest_star/slot, PATCH /guest_star/slot_settings). These records deserialize straight from Twitch's
// snake_case JSON via the transport's naming policy — no per-property annotations. Twitch ids stay
// strings (guests are other users); timestamps are DateTimeOffset; the owning tenant is always passed
// in as a Guid method argument, never modelled here. Guest Star is a beta feature, so these shapes
// track the current reference and may move while Twitch iterates.

/// <summary>
/// Get Channel Guest Star Settings — the host's Guest Star configuration: whether moderators can send
/// guests live, the slot count, browser-source audio, the group layout, and the browser-source token.
/// </summary>
public sealed record TwitchGuestStarChannelSettings(
    bool IsModeratorSendLiveEnabled,
    int SlotCount,
    bool IsBrowserSourceAudioEnabled,
    string GroupLayout,
    string BrowserSourceToken
);

/// <summary>
/// Per-slot audio or video state for a guest in a session: whether the host enabled it, whether the
/// guest enabled it, and whether the source is available to the guest at all.
/// </summary>
public sealed record TwitchGuestStarMediaSettings(
    bool IsHostEnabled,
    bool IsGuestEnabled,
    bool IsAvailable
);

/// <summary>
/// One guest occupying a slot in an active Guest Star session — their slot id and live state, identity,
/// volume, when they were assigned, and the per-slot audio/video settings.
/// </summary>
public sealed record TwitchGuestStarGuest(
    string SlotId,
    bool IsLive,
    string UserId,
    string UserDisplayName,
    string UserLogin,
    int Volume,
    DateTimeOffset AssignedAt,
    TwitchGuestStarMediaSettings AudioSettings,
    TwitchGuestStarMediaSettings VideoSettings
);

/// <summary>
/// An ongoing Guest Star session (Get / Create / End Guest Star Session responses) — the session id and
/// the guests currently in its slots.
/// </summary>
public sealed record TwitchGuestStarSession(string Id, IReadOnlyList<TwitchGuestStarGuest> Guests);

/// <summary>
/// One pending invite to a Guest Star session (Get Guest Star Invites) — the invitee, when they were
/// invited, their join status, and their video/audio enabled/availability flags while in the waiting
/// room.
/// </summary>
public sealed record TwitchGuestStarInvite(
    string UserId,
    DateTimeOffset InvitedAt,
    string Status,
    bool IsVideoEnabled,
    bool IsAudioEnabled,
    bool IsVideoAvailable,
    bool IsAudioAvailable
);

/// <summary>
/// Update Channel Guest Star Settings request body. All fields optional — only the ones set are sent
/// (the transport omits nulls), matching Twitch's "patch only what you provide" semantics. The
/// broadcaster is the Guid method argument, not part of this body.
/// </summary>
public sealed record UpdateGuestStarSettingsRequest(
    bool? IsModeratorSendLiveEnabled = null,
    int? SlotCount = null,
    bool? IsBrowserSourceAudioEnabled = null,
    string? GroupLayout = null,
    bool? RegenerateBrowserSources = null
);
