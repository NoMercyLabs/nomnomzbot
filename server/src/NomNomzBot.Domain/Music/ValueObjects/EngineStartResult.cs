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
