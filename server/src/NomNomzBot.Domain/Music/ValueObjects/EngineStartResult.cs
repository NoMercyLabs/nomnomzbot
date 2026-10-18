// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Music.ValueObjects;

/// <summary>What an engine answers to Start. A refusal carries the reason; it is data, not an exception.</summary>
public sealed record EngineStartResult(bool Accepted, string? FailureReason = null)
{
    public static EngineStartResult Ok { get; } = new(true);

    public static EngineStartResult Refused(string reason) => new(false, reason);
}
