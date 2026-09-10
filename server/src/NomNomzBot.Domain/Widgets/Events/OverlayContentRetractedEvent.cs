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

namespace NomNomzBot.Domain.Widgets.Events;

/// <summary>
/// Content already rendered by an overlay/widget must be pulled off-screen because its source was
/// removed by moderation (widgets-overlays.md §2a) — a deleted chat message, or everything from a
/// timed-out/banned author. <see cref="AuthorUserId"/> is the RESOLVED local user id (null when the
/// platform-native author has no local <c>User</c> row yet); the wire-facing <c>RetractPayload</c>
/// carries the platform-native id instead, since that is what a rendered widget already attached to
/// its content. Retracting content that is already gone is a no-op downstream, never an error.
/// </summary>
public sealed class OverlayContentRetractedEvent : DomainEventBase
{
    /// <summary>The platform message id, when the trigger was one message (a delete).</summary>
    public string? SourceMessageId { get; init; }

    /// <summary>Set for a timeout/ban/nuke — retract everything from this author.</summary>
    public Guid? AuthorUserId { get; init; }

    /// <summary>message_deleted | user_timeout | user_ban | mod_retract.</summary>
    public required string Reason { get; init; }

    /// <summary>The moderator/operator who caused the retraction; null when automated.</summary>
    public Guid? RetractedByUserId { get; init; }
}
