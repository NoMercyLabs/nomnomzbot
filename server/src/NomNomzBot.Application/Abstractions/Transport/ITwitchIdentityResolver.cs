// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Transport;

/// <summary>
/// Translates between the internal tenant/user surrogate <see cref="Guid"/> keys (schema §1.1) and the
/// external Twitch string ids (<c>Channels.TwitchChannelId</c> / <c>Users.TwitchUserId</c>).
///
/// THE HARD INVARIANT: Twitch never receives a <see cref="Guid"/>. Every Helix / IRC / EventSub call uses
/// the Twitch <c>string</c> id, resolved here from the tenant/user Guid. The Guid is the internal DB key /
/// tenant key only. Inbound Twitch notifications are resolved the other way — Twitch id ⇒ tenant Guid —
/// before any tenant-scoped event is published.
/// </summary>
public interface ITwitchIdentityResolver
{
    /// <summary>
    /// Resolves a tenant (channel) <see cref="Guid"/> to its <c>Channels.TwitchChannelId</c> — the value
    /// every Helix <c>broadcaster_id</c> / EventSub condition / token lookup must use. Returns null when the
    /// channel does not exist (caller must skip the Twitch call rather than send a Guid).
    /// </summary>
    Task<string?> GetTwitchChannelIdAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>
    /// Resolves an inbound Twitch broadcaster/channel id to the owning tenant <see cref="Guid"/>
    /// (<c>Channels.Id</c> where <c>TwitchChannelId == twitchChannelId</c>). Returns null when no channel
    /// is registered for that Twitch id — the notification is for a channel this instance does not manage.
    /// </summary>
    Task<Guid?> GetBroadcasterIdAsync(string twitchChannelId, CancellationToken ct = default);

    /// <summary>
    /// Resolves an inbound Twitch channel <c>login name</c> (used by IRC, which keys by name not id) to the
    /// owning tenant <see cref="Guid"/> (<c>Channels.NameNormalized</c>). Returns null when unknown.
    /// </summary>
    Task<Guid?> GetBroadcasterIdByNameAsync(string channelName, CancellationToken ct = default);

    /// <summary>
    /// Resolves a user (<c>Users.Id</c>) <see cref="Guid"/> to its <c>Users.TwitchUserId</c> — the value
    /// every Helix <c>user_id</c> / <c>moderator_id</c> must use. Returns null when the user does not exist.
    /// </summary>
    Task<string?> GetTwitchUserIdAsync(Guid userId, CancellationToken ct = default);
}
