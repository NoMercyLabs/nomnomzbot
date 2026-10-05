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
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// <c>!help</c> (legacy parity) — with no argument, replies with the usage line. With a command name
/// (<c>!help sr</c>) it answers with that command's real <see cref="CommandDto.Description"/>; a command that
/// exists (authored, or an enabled built-in) but has no description says so; an enabled built-in that has
/// a shipped help line (<see cref="ToneTemplateCatalog"/>, the old bot's wording) answers that line; an unknown name gets the
/// legacy "Unknown command" line. It never answers a name with the command list. Every branch renders in the
/// channel's personality tone via <see cref="IBuiltinResponseComposer"/>.
/// </summary>
public sealed class HelpBuiltin : IBuiltinCommand
{
    private readonly ICommandService _commands;
    private readonly CommandsBuiltin _commandsListing;
    private readonly IBuiltinResponseComposer _composer;

    public HelpBuiltin(
        ICommandService commands,
        CommandsBuiltin commandsListing,
        IBuiltinResponseComposer composer
    )
    {
        _commands = commands;
        _commandsListing = commandsListing;
        _composer = composer;
    }

    public string BuiltinKey => "help";
    public int DefaultCooldownSeconds => 15;
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        string requestedName = context.Args.Trim().TrimStart('!').Split(' ')[0].ToLowerInvariant();

        if (requestedName.Length == 0)
            return Result.Success(
                await ComposeAsync(
                    context,
                    BuiltinResponseSlots.Help.Usage,
                    requestedName,
                    null,
                    ct
                )
            );

        Result<CommandDto> lookup = await _commands.GetAsync(
            context.BroadcasterId.ToString(),
            requestedName,
            ct
        );
        if (lookup.IsSuccess && !string.IsNullOrWhiteSpace(lookup.Value.Description))
            return Result.Success(
                await ComposeAsync(
                    context,
                    BuiltinResponseSlots.Help.Described,
                    lookup.Value.Name,
                    lookup.Value.Description,
                    ct
                )
            );

        bool isBuiltin =
            !lookup.IsSuccess && await IsEnabledBuiltinAsync(context, requestedName, ct);
        string lineSlot = BuiltinResponseSlots.Help.LineFor(requestedName);
        if (isBuiltin && ToneTemplateCatalog.Contains(BuiltinResponseSlots.Help.Key, lineSlot))
            return Result.Success(await ComposeAsync(context, lineSlot, requestedName, null, ct));

        string slot =
            lookup.IsSuccess || isBuiltin
                ? BuiltinResponseSlots.Help.NoDescription
                : BuiltinResponseSlots.Help.Unknown;
        return Result.Success(await ComposeAsync(context, slot, requestedName, null, ct));
    }

    private async Task<bool> IsEnabledBuiltinAsync(
        BuiltinCommandContext context,
        string name,
        CancellationToken ct
    )
    {
        IReadOnlyList<string> triggers = await _commandsListing.ResolveEnabledTriggersAsync(
            context,
            ct
        );
        return triggers.Contains(context.CommandPrefix + name, StringComparer.OrdinalIgnoreCase);
    }

    private Task<string> ComposeAsync(
        BuiltinCommandContext context,
        string slot,
        string command,
        string? description,
        CancellationToken ct
    )
    {
        Dictionary<string, string> variables = new()
        {
            ["user"] = context.TriggeringUserDisplayName,
            ["command"] = command,
            ["prefix"] = context.CommandPrefix,
        };
        if (description is not null)
            variables["description"] = description;

        return _composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinResponseSlots.Help.Key,
                Slot = slot,
                NeutralFallback = NeutralFallback(
                    slot,
                    context.CommandPrefix,
                    command,
                    description
                ),
                Variables = variables,
            },
            ct
        );
    }

    private static string NeutralFallback(
        string slot,
        string prefix,
        string command,
        string? description
    ) =>
        slot switch
        {
            BuiltinResponseSlots.Help.Usage =>
                $"Use {prefix}help <command> to get help for a specific command, or {prefix}commands to see what's available.",
            BuiltinResponseSlots.Help.Described => $"!{command} — {description}",
            BuiltinResponseSlots.Help.NoDescription =>
                $"{prefix}{command} — no help text yet. Use {prefix}commands to see what's available.",
            _ when ToneTemplateCatalog.ShippedTemplate(BuiltinResponseSlots.Help.Key, slot)
                    is { } shipped => shipped.Replace("{prefix}", prefix),
            _ => $"Unknown command \"{command}\". Use {prefix}commands to see what's available.",
        };
}
