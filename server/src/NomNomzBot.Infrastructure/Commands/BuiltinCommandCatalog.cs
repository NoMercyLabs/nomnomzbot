// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// Singleton catalog built from all <see cref="IBuiltinCommand"/> implementations registered in DI.
/// Catalog membership is defined by code; no DB seed rows.
/// </summary>
public sealed class BuiltinCommandCatalog : IBuiltinCommandCatalog
{
    private readonly IReadOnlyDictionary<string, IBuiltinCommand> _commands;

    public BuiltinCommandCatalog(IEnumerable<IBuiltinCommand> commands)
    {
        Dictionary<string, IBuiltinCommand> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (IBuiltinCommand cmd in commands)
            map[cmd.BuiltinKey] = cmd;
        _commands = map;
    }

    public IReadOnlyCollection<IBuiltinCommand> GetAll() => _commands.Values.ToList();

    public IBuiltinCommand? Get(string builtinKey) =>
        _commands.TryGetValue(builtinKey, out IBuiltinCommand? cmd) ? cmd : null;
}
