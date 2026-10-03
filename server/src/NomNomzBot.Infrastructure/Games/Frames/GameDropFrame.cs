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

/// <summary>A drop-game player landed their marker.</summary>
public sealed record GameDropFrame
{
    [WireLiteral("drop")]
    public string Kind { get; init; } = "drop";

    public required string Player { get; init; }

    public required double Landed { get; init; }

    public required double Distance { get; init; }

    public required bool Hit { get; init; }
}
