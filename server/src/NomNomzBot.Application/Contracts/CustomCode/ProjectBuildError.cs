// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.CustomCode;

/// <summary>One build problem of a rejected project save.</summary>
/// <param name="Code">A stable code for the kind of problem.</param>
/// <param name="Message">The reason in plain words.</param>
/// <param name="File">The project file the problem is in. Null when unknown.</param>
/// <param name="Line">The line in that file. Null when the problem has no single line.</param>
/// <param name="Column">The column in that line. Null when the problem has no single column.</param>
public sealed record ProjectBuildError(
    string Code,
    string Message,
    string? File,
    int? Line,
    int? Column
);
