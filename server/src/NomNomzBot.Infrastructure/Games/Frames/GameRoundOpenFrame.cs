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

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Games.Frames;

/// <summary>The lobby opened. The drop game adds the target its widget draws.</summary>
public sealed record GameRoundOpenFrame
{
    [WireLiteral("round_open")]
    public string Kind { get; init; } = "round_open";

    public required int LobbySeconds { get; init; }

    public double? Target { get; init; }

    public double? Radius { get; init; }
}
