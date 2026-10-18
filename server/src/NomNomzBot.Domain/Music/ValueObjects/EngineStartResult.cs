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

/// <summary>
/// What an engine answers to <c>StartAsync</c>. <see cref="BlockedBy"/> means the start is impossible right
/// now for a typed reason; <see cref="Refused(string)"/> means the engine tried and could not start it.
/// </summary>
public sealed record EngineStartResult(bool Accepted, string? FailureReason = null)
{
    public static EngineStartResult Ok { get; } = new(true);

    /// <summary>Set when the start failed on a missing precondition (see <see cref="EngineBlockReason"/>).</summary>
    public EngineBlockReason? Block { get; init; }

    public static EngineStartResult Refused(string reason) => new(false, reason);

    public static EngineStartResult BlockedBy(EngineBlockReason reason) =>
        new(false, reason.ToString()) { Block = reason };
}
