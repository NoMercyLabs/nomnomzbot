// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
