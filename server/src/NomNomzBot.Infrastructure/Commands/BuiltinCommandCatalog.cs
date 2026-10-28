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

    public IReadOnlyCollection<IBuiltinCommand> GetAll() => [.. _commands.Values];

    public IBuiltinCommand? Get(string builtinKey) =>
        _commands.TryGetValue(builtinKey, out IBuiltinCommand? cmd) ? cmd : null;
}
