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

/// <summary>The round was cancelled and every stake refunded. <c>cancelled</c> stays on the wire for older widgets.</summary>
public sealed record GameCancelledFrame
{
    [WireLiteral("cancelled")]
    public string Kind { get; init; } = "cancelled";

    public bool Cancelled { get; init; } = true;

    public required string Reason { get; init; }
}
