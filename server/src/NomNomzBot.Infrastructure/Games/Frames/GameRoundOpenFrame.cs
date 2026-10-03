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

/// <summary>The lobby opened. Each game adds the setting its widget draws: crash the cap, drop the target, heist the odds.</summary>
public sealed record GameRoundOpenFrame
{
    [WireLiteral("round_open")]
    public string Kind { get; init; } = "round_open";

    public required int LobbySeconds { get; init; }

    public double? MaxMultiplier { get; init; }

    public double? Target { get; init; }

    public double? Radius { get; init; }

    public double? SuccessChance { get; init; }
}
