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

/// <summary>The typed answer of an engine control with no value to return.</summary>
public sealed record EngineControlResult(
    EngineControlOutcome Outcome,
    EngineBlockReason? Block = null
)
{
    public static EngineControlResult Done { get; } = new(EngineControlOutcome.Done);

    public static EngineControlResult NotSupported { get; } =
        new(EngineControlOutcome.NotSupported);

    public static EngineControlResult BlockedBy(EngineBlockReason reason) =>
        new(EngineControlOutcome.Blocked, reason);
}

/// <summary>The typed answer of an engine control that returns a value when it is done.</summary>
public sealed record EngineControlResult<T>(
    EngineControlOutcome Outcome,
    T? Value = default,
    EngineBlockReason? Block = null
)
{
    public static EngineControlResult<T> NotSupported { get; } =
        new(EngineControlOutcome.NotSupported);

    public static EngineControlResult<T> DoneWith(T value) => new(EngineControlOutcome.Done, value);

    public static EngineControlResult<T> BlockedBy(EngineBlockReason reason) =>
        new(EngineControlOutcome.Blocked, default, reason);
}
