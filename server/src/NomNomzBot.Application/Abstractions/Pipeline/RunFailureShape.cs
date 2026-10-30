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

using NomNomzBot.Domain.Commands.Enums;

namespace NomNomzBot.Application.Abstractions.Pipeline;

/// <summary>
/// What kind of failure ended a step or a run, and the limit that stopped it when one did. Built by the code
/// where the failure happens, so nobody reads it back out of an error message. <paramref name="LimitKey"/> is
/// a stable dotted name (for example <c>script.wall_clock_ms</c>) and <paramref name="LimitValue"/> is the
/// value that was in force, in the unit the key names.
/// </summary>
public sealed record RunFailureShape(
    RunFailureKind Kind,
    string? LimitKey = null,
    long? LimitValue = null
);
