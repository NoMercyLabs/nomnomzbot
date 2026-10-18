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
