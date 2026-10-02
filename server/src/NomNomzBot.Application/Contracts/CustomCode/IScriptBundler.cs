// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.CustomCode;

/// <summary>
/// Turns a script project (TypeScript or JavaScript, one or many files) into the ONE plain-JavaScript program the
/// sandbox runs: types are stripped and relative imports are bundled in. A bare (package) import fails — scripts
/// have no npm. A failure names the file, line and column of each problem in <see cref="Result.ErrorMessage"/>.
/// </summary>
public interface IScriptBundler
{
    Task<Result<string>> BundleAsync(
        IReadOnlyDictionary<string, string> files,
        string entry,
        CancellationToken cancellationToken = default
    );
}
