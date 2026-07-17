// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Events;

/// <summary>
/// A ban issued in a channel while it was in an ACTIVE Twitch shared-chat session, offered to the trust
/// web (moderation.md §3.5). Published ONLY when the origin channel opted in
/// (<c>SharedBanSettings.ShareOutgoingBans</c>); partners apply it only if they trust the origin AND share
/// the same session (<see cref="SharedChatSessionId"/>) — the double opt-in. <c>BroadcasterId</c> = the
/// ORIGIN channel.
/// </summary>
public sealed class SharedChatBanIssuedEvent : DomainEventBase
{
    public required string SharedChatSessionId { get; init; }

    /// <summary>The origin tenant channel (same as <c>BroadcasterId</c>, named for the consumer's clarity).</summary>
    public required Guid OriginChannelId { get; init; }

    public required string TargetTwitchUserId { get; init; }

    public string? TargetDisplayName { get; init; }

    public string? Reason { get; init; }
}
