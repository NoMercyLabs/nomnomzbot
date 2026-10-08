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

namespace NomNomzBot.Infrastructure.Games.Frames;

/// <summary>A crash player cashed out at the current multiplier.</summary>
public sealed record GameCashoutFrame
{
    [WireLiteral("cashout")]
    public string Kind { get; init; } = "cashout";

    public required string Player { get; init; }

    public required double Multiplier { get; init; }

    public required long Payout { get; init; }

    public required int CrewSize { get; init; }
}
