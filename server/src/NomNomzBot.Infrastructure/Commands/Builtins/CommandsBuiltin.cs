// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// <c>!commands</c> (legacy parity, S068b) — lists the channel's currently enabled trigger words, both
/// authored custom commands (<see cref="ICommandService"/>) and code-defined built-ins
/// (<see cref="IBuiltinCommandService"/>). Reuses the same two read paths the dashboard's Commands screen
/// already queries — this never maintains its own copy of "what commands exist". Renders in the channel's
/// personality tone via <see cref="IBuiltinResponseComposer"/>, same as every other response built-in.
/// </summary>
public sealed class CommandsBuiltin : IBuiltinCommand
{
    private readonly ICommandService _commands;
    private readonly IServiceProvider _serviceProvider;
    private readonly IBuiltinResponseComposer _composer;

    // IBuiltinCommandService is resolved lazily from IServiceProvider rather than taken as a
    // constructor dependency: this built-in is itself collected into IBuiltinCommandCatalog's
    // IEnumerable<IBuiltinCommand>, which IBuiltinCommandService's own implementation depends on
    // (catalog -> this builtin -> builtin command service -> catalog). A direct constructor
    // dependency here creates a circular DI graph that fails ServiceProvider validation at
    // startup; deferring resolution to call time (this class never sits inside the catalog's own
    // construction path) breaks the cycle without changing runtime behavior.
    public CommandsBuiltin(
        ICommandService commands,
        IServiceProvider serviceProvider,
        IBuiltinResponseComposer composer
    )
    {
        _commands = commands;
        _serviceProvider = serviceProvider;
        _composer = composer;
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

        string reply = await ComposeListingAsync(context, triggers, ct);
        return Result.Success(reply);
    }

    /// <summary>
    /// Renders the tone-styled listing reply (or empty-state reply) for a resolved trigger set — shared by
    /// <see cref="CommandsBuiltin"/> and <see cref="HelpBuiltin"/>'s generic fallback so both phrase
    /// identically.
    /// </summary>
    internal Task<string> ComposeListingAsync(
        BuiltinCommandContext context,
        IReadOnlyList<string> triggers,
        CancellationToken ct
    )
    {
        if (triggers.Count == 0)
            return _composer.ComposeAsync(
                new()
                {
                    BroadcasterId = context.BroadcasterId,
                    Personality = context.Personality,
                    BuiltinKey = BuiltinResponseSlots.Commands.Key,
                    Slot = BuiltinResponseSlots.Commands.Empty,
                    NeutralFallback =
                        $"@{context.TriggeringUserDisplayName} there are no commands enabled in this channel yet.",
                    Variables = new Dictionary<string, string>
                    {
                        ["user"] = context.TriggeringUserDisplayName,
                    },
                },
                ct
            );

        return _composer.ComposeAsync(
            new()
            {
                BroadcasterId = context.BroadcasterId,
                Personality = context.Personality,
                BuiltinKey = BuiltinResponseSlots.Commands.Key,
                Slot = BuiltinResponseSlots.Commands.List,
                OverrideTemplate = context.CustomResponseTemplate,
                NeutralFallback =
                    $"@{context.TriggeringUserDisplayName} available commands: {string.Join(", ", triggers)}",
                Variables = new Dictionary<string, string>
                {
                    ["user"] = context.TriggeringUserDisplayName,
                    ["commands"] = string.Join(", ", triggers),
                },
            },
            ct
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

        IBuiltinCommandService builtins =
            _serviceProvider.GetRequiredService<IBuiltinCommandService>();
        Result<IReadOnlyList<BuiltinCommandDto>> builtinResult = await builtins.ListAsync(
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
