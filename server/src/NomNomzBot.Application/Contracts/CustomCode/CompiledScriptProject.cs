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

/// <summary>A project that passed the save gate, compiled but not stored.</summary>
/// <param name="CompiledJs">The runnable bundle.</param>
/// <param name="CompiledHash">The hash of the bundle.</param>
/// <param name="DeclaredCapabilities">The capabilities the compile found the script declares.</param>
public sealed record CompiledScriptProject(
    string CompiledJs,
    string CompiledHash,
    IReadOnlyList<string> DeclaredCapabilities
);
