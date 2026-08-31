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
/// <c>!help</c> (legacy parity, S068b) — with no argument, replies with the same enabled-trigger listing as
/// <see cref="CommandsBuiltin"/>. With a command name argument (<c>!help sr</c>), looks that authored command
/// up via <see cref="ICommandService.GetAsync"/> and replies with its real <see cref="CommandDto.Description"/>
/// when one is set; a built-in has no description field on <see cref="BuiltinCommandDto"/>, and an unknown or
/// undescribed name falls back to the same generic listing rather than answering with nothing useful.
/// </summary>
public sealed class HelpBuiltin : IBuiltinCommand
{
    private readonly ICommandService _commands;
    private readonly CommandsBuiltin _commandsListing;

    public HelpBuiltin(ICommandService commands, CommandsBuiltin commandsListing)
    {
        _commands = commands;
        _commandsListing = commandsListing;
    }

    public string BuiltinKey => "help";
    public int DefaultCooldownSeconds => 15;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        string requestedName = context.Args.Trim().TrimStart('!').Split(' ')[0];

        if (requestedName.Length > 0)
        {
            Result<CommandDto> lookup = await _commands.GetAsync(
                context.BroadcasterId.ToString(),
                requestedName,
                ct
            );
            if (lookup.IsSuccess && !string.IsNullOrWhiteSpace(lookup.Value.Description))
                return Result.Success(
                    $"@{context.TriggeringUserDisplayName} !{lookup.Value.Name}: {lookup.Value.Description}"
                );
        }

        IReadOnlyList<string> triggers = await _commandsListing.ResolveEnabledTriggersAsync(
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
}
