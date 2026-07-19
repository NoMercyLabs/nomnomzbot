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

public sealed class UserUnbannedEvent : DomainEventBase
{
    public required string TargetUserId { get; init; }

    /// <summary>The unbanned viewer's display name (<c>user_name</c>); null on non-Twitch ingests.</summary>
    public string? TargetDisplayName { get; init; }

    public required string ModeratorUserId { get; init; }

    /// <summary>The acting moderator's display name (<c>moderator_user_name</c>); null on non-Twitch ingests.</summary>
    public string? ModeratorDisplayName { get; init; }
}
