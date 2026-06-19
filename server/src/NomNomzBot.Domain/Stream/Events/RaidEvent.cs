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

namespace NomNomzBot.Domain.Stream.Events;

/// <summary>
/// Published when this channel receives a raid.
/// </summary>
public sealed class RaidEvent : DomainEventBase
{
    public required string FromUserId { get; init; }
    public required string FromDisplayName { get; init; }
    public required string FromLogin { get; init; }
    public required int ViewerCount { get; init; }
}
