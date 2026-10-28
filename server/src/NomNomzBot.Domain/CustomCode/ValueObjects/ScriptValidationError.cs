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

namespace NomNomzBot.Domain.CustomCode.ValueObjects;

/// <summary>One save-time validation rejection (custom-code.md §4).</summary>
/// <param name="Code">A stable code for the kind of rejection, for programs to match on.</param>
/// <param name="Message">The reason in plain words, for the author to read.</param>
/// <param name="Line">The line in the script where the problem is. Null when it has no single line.</param>
/// <param name="Column">The column in that line where the problem starts. Null when it has no single column.</param>
/// <param name="File">The project file the line and column belong to. Null when unknown (older stored errors).</param>
public sealed record ScriptValidationError(
    string Code,
    string Message,
    int? Line,
    int? Column,
    string? File = null
);
