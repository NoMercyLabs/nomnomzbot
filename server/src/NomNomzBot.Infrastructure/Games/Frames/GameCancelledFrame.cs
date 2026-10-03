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

/// <summary>The round was cancelled and every stake refunded. <c>cancelled</c> stays on the wire for older widgets.</summary>
public sealed record GameCancelledFrame
{
    [WireLiteral("cancelled")]
    public string Kind { get; init; } = "cancelled";

    public bool Cancelled { get; init; } = true;

    public required string Reason { get; init; }
}
