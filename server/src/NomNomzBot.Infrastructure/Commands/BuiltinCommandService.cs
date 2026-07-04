// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Commands;

public sealed class BuiltinCommandService : IBuiltinCommandService
{
    private readonly IBuiltinCommandCatalog _catalog;
    private readonly IApplicationDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly IChannelRegistry _registry;

    public BuiltinCommandService(
        IBuiltinCommandCatalog catalog,
        IApplicationDbContext db,
        IEventBus eventBus,
        IChannelRegistry registry
    )
    {
        _catalog = catalog;
        _db = db;
        _eventBus = eventBus;
        _registry = registry;
    }

    public async Task<Result<IReadOnlyList<BuiltinCommandDto>>> ListAsync(
        string broadcasterId,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<IReadOnlyList<BuiltinCommandDto>>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        // Load all toggle rows for this channel (absent = enabled with catalog defaults).
        Dictionary<string, ChannelBuiltinCommand> toggles = await _db
            .ChannelBuiltinCommands.Where(c => c.BroadcasterId == broadcaster)
            .ToDictionaryAsync(c => c.BuiltinKey, c => c, StringComparer.OrdinalIgnoreCase, ct);

        List<BuiltinCommandDto> dtos = _catalog
            .GetAll()
            .Select(cmd =>
            {
                bool isEnabled =
                    !toggles.TryGetValue(cmd.BuiltinKey, out ChannelBuiltinCommand? toggle)
                    || toggle.IsEnabled;

                return new BuiltinCommandDto(
                    cmd.BuiltinKey,
                    "!" + cmd.BuiltinKey,
                    isEnabled,
                    cmd.DefaultCooldownSeconds,
                    cmd.DefaultMinPermissionLevel
                );
            })
            .ToList();

        return Result.Success<IReadOnlyList<BuiltinCommandDto>>(dtos);
    }

    public async Task<Result> SetEnabledAsync(
        string broadcasterId,
        string builtinKey,
        bool enabled,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure($"Invalid channel ID '{broadcasterId}'.", "VALIDATION_FAILED");

        if (_catalog.Get(builtinKey) is null)
            return Result.Failure($"Unknown built-in command '{builtinKey}'.", "NOT_FOUND");

        ChannelBuiltinCommand? existing = await _db.ChannelBuiltinCommands.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcaster && c.BuiltinKey == builtinKey,
            ct
        );

        if (existing is null)
        {
            _db.ChannelBuiltinCommands.Add(
                new ChannelBuiltinCommand
                {
                    BroadcasterId = broadcaster,
                    BuiltinKey = builtinKey,
                    IsEnabled = enabled,
                }
            );
        }
        else
        {
            existing.IsEnabled = enabled;
        }

        await _db.SaveChangesAsync(ct);
        await _registry.InvalidateBuiltinsAsync(broadcaster, ct);
        await _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcaster,
                Domain = "builtins",
                EntityId = builtinKey,
                Action = "toggled",
            },
            ct
        );
        return Result.Success();
    }
}
