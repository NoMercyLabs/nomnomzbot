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
/// A custom chat filter matched a message, but the platform refused the action the filter asked for (delete,
/// timeout, warn or ban). The message or viewer is still in chat, so a human has to act. Published so the
/// action-required inbox can show it (the journal keeps it). <c>BroadcasterId</c> = the channel the filter runs in.
/// </summary>
public sealed class ChatFilterActionFailedEvent : DomainEventBase
{
    /// <summary>The filter that matched.</summary>
    public required Guid FilterId { get; init; }

    /// <summary>The filter's name at the time, as the streamer sees it.</summary>
    public required string FilterName { get; init; }

    /// <summary>The Twitch user id of the chatter whose message matched (a number as text).</summary>
    public required string SubjectTwitchUserId { get; init; }

    /// <summary>The chatter's login name, when it is known.</summary>
    public string? SubjectUsername { get; init; }

    /// <summary>The action the platform refused: <c>delete</c>, <c>timeout</c>, <c>warn</c> or <c>ban</c>.</summary>
    public required string Action { get; init; }

    /// <summary>Why the platform call failed, as the platform reported it.</summary>
    public required string Error { get; init; }
}
