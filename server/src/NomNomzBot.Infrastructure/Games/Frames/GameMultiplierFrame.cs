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

/// <summary>Crash's running multiplier: a tick (<c>progress</c>), the bust, or the cap being reached.</summary>
public sealed record GameMultiplierFrame
{
    [WireLiteral("progress", "bust", "cap")]
    public required string Kind { get; init; }

    public required double Multiplier { get; init; }
}
