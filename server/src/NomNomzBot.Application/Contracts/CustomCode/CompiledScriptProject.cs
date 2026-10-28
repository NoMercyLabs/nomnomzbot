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

/// <summary>A project that passed the save gate, compiled but not stored.</summary>
/// <param name="CompiledJs">The runnable bundle.</param>
/// <param name="CompiledHash">The hash of the bundle.</param>
/// <param name="DeclaredCapabilities">The capabilities the compile found the script declares.</param>
public sealed record CompiledScriptProject(
    string CompiledJs,
    string CompiledHash,
    IReadOnlyList<string> DeclaredCapabilities
);
