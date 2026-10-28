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
