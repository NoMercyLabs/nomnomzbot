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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// <c>!commands</c> (legacy parity, S068b) — lists the channel's currently enabled trigger words, both
/// authored custom commands (<see cref="ICommandService"/>) and code-defined built-ins
/// (<see cref="IBuiltinCommandService"/>). Reuses the same two read paths the dashboard's Commands screen
/// already queries — this never maintains its own copy of "what commands exist".
/// </summary>
public sealed class CommandsBuiltin : IBuiltinCommand
{
    private readonly ICommandService _commands;
    private readonly IBuiltinCommandService _builtins;

    public CommandsBuiltin(ICommandService commands, IBuiltinCommandService builtins)
    {
        _commands = commands;
        _builtins = builtins;
    }

    public string BuiltinKey => "commands";
    public int DefaultCooldownSeconds => 15;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        IReadOnlyList<string> triggers = await ResolveEnabledTriggersAsync(
            context.BroadcasterId,
            ct
        );

        if (triggers.Count == 0)
            return Result.Success(
                $"@{context.TriggeringUserDisplayName} there are no commands enabled in this channel yet."
            );

        return Result.Success(
            $"@{context.TriggeringUserDisplayName} available commands: {string.Join(", ", triggers)}"
        );
    }

    /// <summary>
    /// Merges enabled authored-command names with enabled built-in keys into one sorted, de-duplicated
    /// trigger list. Shared by <see cref="CommandsBuiltin"/> and <see cref="HelpBuiltin"/>'s generic fallback.
    /// </summary>
    internal async Task<IReadOnlyList<string>> ResolveEnabledTriggersAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        string broadcasterIdText = broadcasterId.ToString();

        Result<PagedList<CommandListItem>> customResult = await _commands.ListAsync(
            broadcasterIdText,
            new PaginationParams(Page: 1, PageSize: PaginationParams.MaxPageSize),
            ct
        );
        IEnumerable<string> customNames = customResult.IsSuccess
            ? customResult.Value.Items.Where(c => c.IsEnabled).Select(c => c.Name)
            : [];

        Result<IReadOnlyList<BuiltinCommandDto>> builtinResult = await _builtins.ListAsync(
            broadcasterIdText,
            ct
        );
        IEnumerable<string> builtinKeys = builtinResult.IsSuccess
            ? builtinResult.Value.Where(b => b.IsEnabled).Select(b => b.BuiltinKey)
            : [];

        return customNames
            .Concat(builtinKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
