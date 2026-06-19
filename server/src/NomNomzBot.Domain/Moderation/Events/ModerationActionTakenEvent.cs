// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Moderation.Events;

using NomNomzBot.Domain.Platform;

public sealed class ModerationActionTakenEvent : DomainEventBase
{
    public required string ChannelId { get; init; }
    public required string ModeratorId { get; init; }
    public required string TargetUserId { get; init; }
    public required string ActionType { get; init; }
    public required string? Reason { get; init; }
}
