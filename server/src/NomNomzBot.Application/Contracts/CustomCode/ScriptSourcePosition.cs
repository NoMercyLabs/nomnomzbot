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

/// <summary>
/// A place in the author's own source: the project file, and the line and column in it. Both numbers start at 1,
/// the way an editor shows them. <see cref="File"/> is null when the file is not known.
/// </summary>
public sealed record ScriptSourcePosition(string? File, int Line, int Column);

/// <summary>One problem the script build found, with where it is in the author's source. Carried as the
/// <c>ErrorData</c> of a failed <see cref="IScriptBundler"/> result.</summary>
public sealed record ScriptBuildError(string Message, ScriptSourcePosition Position);
