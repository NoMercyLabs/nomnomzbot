// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
