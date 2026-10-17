// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Dashboard.Stats;

/// <summary>One stream's time window: <c>StartedAt</c> to <c>EndedAt</c>, or to now while it is live.</summary>
public sealed record StreamWindow(
    Guid ChannelId,
    string StreamId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset End,
    int? PeakViewers,
    string? Title,
    string? Game
)
{
    public DateTime From => StartedAt.UtcDateTime;

    public DateTime To => End.UtcDateTime;
}
