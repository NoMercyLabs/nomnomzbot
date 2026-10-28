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

namespace NomNomzBot.Application.Commands.Builtin;

/// <summary>
/// Static registry of code-defined built-in commands. Singleton — populated from DI at startup.
/// </summary>
public interface IBuiltinCommandCatalog
{
    IReadOnlyCollection<IBuiltinCommand> GetAll();
    IBuiltinCommand? Get(string builtinKey);
}
