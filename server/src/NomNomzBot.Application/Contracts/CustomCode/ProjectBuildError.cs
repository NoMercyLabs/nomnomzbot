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
