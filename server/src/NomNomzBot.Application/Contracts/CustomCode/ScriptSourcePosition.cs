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

/// <summary>
/// A place in the author's own source: the project file, and the line and column in it. Both numbers start at 1,
/// the way an editor shows them. <see cref="File"/> is null when the file is not known.
/// </summary>
public sealed record ScriptSourcePosition(string? File, int Line, int Column);

/// <summary>One problem the script build found, with where it is in the author's source. Carried as the
/// <c>ErrorData</c> of a failed <see cref="IScriptBundler"/> result.</summary>
public sealed record ScriptBuildError(string Message, ScriptSourcePosition Position);
