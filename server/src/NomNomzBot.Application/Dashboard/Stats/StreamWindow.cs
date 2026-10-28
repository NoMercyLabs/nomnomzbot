// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
