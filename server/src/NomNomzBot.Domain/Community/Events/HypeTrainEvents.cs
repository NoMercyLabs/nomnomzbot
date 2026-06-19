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

namespace NomNomzBot.Domain.Community.Events;

/// <summary>Published when a hype train begins.</summary>
public sealed class HypeTrainBeganEvent : DomainEventBase
{
    public required string HypeTrainId { get; init; }
    public required int Level { get; init; }
    public required int Total { get; init; }
    public required int Goal { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Published when a hype train ends.</summary>
public sealed class HypeTrainEndedEvent : DomainEventBase
{
    public required string HypeTrainId { get; init; }
    public required int Level { get; init; }
    public required int Total { get; init; }
}
