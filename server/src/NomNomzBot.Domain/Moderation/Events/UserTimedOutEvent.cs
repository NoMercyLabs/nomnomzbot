// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Moderation.Events;

/// <summary>When a viewer is timed out in the channel.</summary>
public sealed class UserTimedOutEvent : DomainEventBase, IProviderScopedEvent
{
    /// <summary>The platform this timeout was delivered by. Defaults to Twitch, the dominant source.</summary>
    public string Provider { get; init; } = AuthEnums.Platform.Twitch;

    /// <summary>The id of the timed-out viewer (a number as text on Twitch).</summary>
    public required string TargetUserId { get; init; }

    /// <summary>The timed-out viewer's display name, as shown in chat.</summary>
    public required string TargetDisplayName { get; init; }

    /// <summary>The id of the moderator who set the timeout (a number as text on Twitch).</summary>
    public required string ModeratorUserId { get; init; }

    /// <summary>The acting moderator's display name (<c>moderator_user_name</c>); null on non-Twitch ingests.</summary>
    public string? ModeratorDisplayName { get; init; }

    /// <summary>How long the timeout lasts, in seconds.</summary>
    public required int DurationSeconds { get; init; }

    /// <summary>The reason given for the timeout. Empty when no reason was given.</summary>
    public string? Reason { get; init; }
}
