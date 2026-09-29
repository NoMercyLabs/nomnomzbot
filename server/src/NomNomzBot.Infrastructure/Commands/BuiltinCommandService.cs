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
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands.Builtins;

namespace NomNomzBot.Infrastructure.Commands;

public sealed class BuiltinCommandService : IBuiltinCommandService
{
    /// <summary>The longest cooldown a channel can give a built-in (one hour).</summary>
    public const int MaxCooldownSeconds = 3600;

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

        // Absent row = enabled with catalog defaults.
        List<ChannelBuiltinCommand> rows = await RowsAsync(broadcaster, ct);
        List<BuiltinCommandDto> dtos = [.. _catalog.GetAll().Select(cmd => ToDto(cmd, rows))];

        return Result.Success<IReadOnlyList<BuiltinCommandDto>>(dtos);
    }

    public async Task<Result<BuiltinCommandDto>> GetAsync(
        string broadcasterId,
        string builtinKey,
        CancellationToken ct = default
    )
    {
        Result<(Guid Broadcaster, IBuiltinCommand Command)> target = Resolve(
            broadcasterId,
            builtinKey
        );
        if (target.IsFailure)
            return Result.Failure<BuiltinCommandDto>(target.ErrorMessage!, target.ErrorCode!);

        List<ChannelBuiltinCommand> rows = await RowsAsync(target.Value.Broadcaster, ct);
        return Result.Success(ToDto(target.Value.Command, rows));
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

        IBuiltinCommand? command = _catalog.Get(builtinKey);
        if (command is null)
            return Result.Failure($"Unknown built-in command '{builtinKey}'.", "NOT_FOUND");

        // The data-subject rights floor (gdpr-crypto.md §9) is always-on: reserved built-ins cannot
        // be disabled (or pointlessly toggled) by any channel.
        if (command.IsReserved)
            return Result.Failure(
                $"'{builtinKey}' is a reserved data-rights command — it is always on and cannot be toggled.",
                "VALIDATION_FAILED"
            );

        ChannelBuiltinCommand? existing = await _db.ChannelBuiltinCommands.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcaster && c.BuiltinKey == builtinKey,
            ct
        );

        if (existing is null)
        {
            _db.ChannelBuiltinCommands.Add(
                new()
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

        await SaveAndAnnounceAsync(broadcaster, builtinKey, "toggled", ct);
        return Result.Success();
    }

    /// <summary>
    /// Sets the channel's per-command "speak with TTS" override for a built-in (S-OBS-12) — same
    /// <c>OverridesJson</c> blob as the channel's reply texts (<see cref="BuiltinReplyService"/>), read-merged so
    /// setting one never clears the other.
    /// </summary>
    public async Task<Result> SetSpeakWithTtsAsync(
        string broadcasterId,
        string builtinKey,
        bool enabled,
        CancellationToken ct = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure($"Invalid channel ID '{broadcasterId}'.", "VALIDATION_FAILED");

        IBuiltinCommand? command = _catalog.Get(builtinKey);
        if (command is null)
            return Result.Failure($"Unknown built-in command '{builtinKey}'.", "NOT_FOUND");

        if (command.IsReserved)
            return Result.Failure(
                $"'{builtinKey}' is a reserved data-rights command — it does not support TTS.",
                "VALIDATION_FAILED"
            );

        await WriteOverridesAsync(
            broadcaster,
            builtinKey,
            overrides => overrides with { SpeakWithTts = enabled },
            ct
        );
        await SaveAndAnnounceAsync(broadcaster, builtinKey, "speak_with_tts_set", ct);
        return Result.Success();
    }

    public async Task<Result<BuiltinCommandDto>> UpdateSettingsAsync(
        string broadcasterId,
        string builtinKey,
        BuiltinSettingsUpdate update,
        CancellationToken ct = default
    )
    {
        Result<(Guid Broadcaster, IBuiltinCommand Command)> target = Resolve(
            broadcasterId,
            builtinKey
        );
        if (target.IsFailure)
            return Result.Failure<BuiltinCommandDto>(target.ErrorMessage!, target.ErrorCode!);

        (Guid broadcaster, IBuiltinCommand command) = target.Value;
        if (command.IsReserved)
            return Result.Failure<BuiltinCommandDto>(
                $"'{builtinKey}' is a reserved data-rights command — its cooldown and permission are fixed.",
                "VALIDATION_FAILED"
            );

        if (update.CooldownSeconds is < 0 or > MaxCooldownSeconds)
            return Result.Failure<BuiltinCommandDto>(
                $"A cooldown must be between 0 and {MaxCooldownSeconds} seconds.",
                "VALIDATION_FAILED"
            );

        int? floor = null;
        if (update.MinPermissionLevel is not null)
        {
            floor = PermissionLevelNames.ToLevelValue(update.MinPermissionLevel);
            if (floor is null)
                return Result.Failure<BuiltinCommandDto>(
                    $"'{update.MinPermissionLevel}' is not a permission level. Use one of: {string.Join(", ", PermissionLevelNames.All)}.",
                    "VALIDATION_FAILED"
                );

            if (floor < command.DefaultMinPermissionLevel)
                return Result.Failure<BuiltinCommandDto>(
                    $"!{command.BuiltinKey} needs at least {PermissionLevelNames.ToName(command.DefaultMinPermissionLevel)} — that floor keeps it safe and cannot be lowered.",
                    "VALIDATION_FAILED"
                );
        }

        // A value equal to the catalogue's is no override — the row stays "untouched" for platform updates.
        int? cooldown =
            update.CooldownSeconds == command.DefaultCooldownSeconds
                ? null
                : update.CooldownSeconds;
        int? minPermission = floor == command.DefaultMinPermissionLevel ? null : floor;

        await WriteOverridesAsync(
            broadcaster,
            command.BuiltinKey,
            overrides =>
                overrides with
                {
                    CooldownSeconds = cooldown,
                    MinPermissionLevel = minPermission,
                },
            ct
        );
        await SaveAndAnnounceAsync(broadcaster, command.BuiltinKey, "settings_set", ct);

        return Result.Success(ToDto(command, await RowsAsync(broadcaster, ct)));
    }

    public async Task<Result<BuiltinCommandDto>> ResetAsync(
        string broadcasterId,
        string builtinKey,
        CancellationToken ct = default
    )
    {
        Result<(Guid Broadcaster, IBuiltinCommand Command)> target = Resolve(
            broadcasterId,
            builtinKey
        );
        if (target.IsFailure)
            return Result.Failure<BuiltinCommandDto>(target.ErrorMessage!, target.ErrorCode!);

        (Guid broadcaster, IBuiltinCommand command) = target.Value;
        string key = Normalize(command.BuiltinKey);
        string replyGroup = BuiltinResponseSlots.ReplyGroupFor(key);

        foreach (ChannelBuiltinCommand row in await RowsAsync(broadcaster, ct))
        {
            string rowKey = Normalize(row.BuiltinKey);
            if (rowKey == key)
            {
                // The command's own row: everything back to default.
                row.IsEnabled = true;
                row.OverridesJson = null;
            }
            else if (BuiltinResponseSlots.ReplyGroupFor(rowKey) == replyGroup)
            {
                // A row sharing the reply group (e.g. lurk for !unlurk) loses only the replies; its own
                // enabled state, TTS, cooldown and floor belong to the other command.
                row.OverridesJson = BuiltinOverridesJson.Serialize(
                    BuiltinOverridesJson.Read(rowKey, row.OverridesJson) with
                    {
                        Responses = new Dictionary<string, string>(),
                    }
                );
            }
        }

        await SaveAndAnnounceAsync(broadcaster, command.BuiltinKey, "reset", ct);
        return Result.Success(ToDto(command, await RowsAsync(broadcaster, ct)));
    }

    private Result<(Guid Broadcaster, IBuiltinCommand Command)> Resolve(
        string broadcasterId,
        string builtinKey
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Result.Failure<(Guid, IBuiltinCommand)>(
                $"Invalid channel ID '{broadcasterId}'.",
                "VALIDATION_FAILED"
            );

        IBuiltinCommand? command = _catalog.Get(builtinKey);
        return command is null
            ? Result.Failure<(Guid, IBuiltinCommand)>(
                $"Unknown built-in command '{builtinKey}'.",
                "NOT_FOUND"
            )
            : Result.Success((broadcaster, command));
    }

    private Task<List<ChannelBuiltinCommand>> RowsAsync(Guid broadcaster, CancellationToken ct) =>
        _db.ChannelBuiltinCommands.Where(c => c.BroadcasterId == broadcaster).ToListAsync(ct);

    /// <summary>
    /// Read-merge-write of one built-in's overrides blob: <paramref name="change"/> sees every stored field, so
    /// changing one setting keeps the rest (replies, TTS, cooldown, floor). A change that leaves nothing to store
    /// on a missing row adds no row.
    /// </summary>
    private async Task WriteOverridesAsync(
        Guid broadcaster,
        string builtinKey,
        Func<BuiltinOverrides, BuiltinOverrides> change,
        CancellationToken ct
    )
    {
        ChannelBuiltinCommand? existing = await _db.ChannelBuiltinCommands.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcaster && c.BuiltinKey == builtinKey,
            ct
        );

        string? overridesJson = BuiltinOverridesJson.Serialize(
            change(BuiltinOverridesJson.Read(builtinKey, existing?.OverridesJson))
        );

        if (existing is not null)
        {
            existing.OverridesJson = overridesJson;
            return;
        }

        if (overridesJson is null)
            return;

        _db.ChannelBuiltinCommands.Add(
            new()
            {
                BroadcasterId = broadcaster,
                BuiltinKey = builtinKey,
                IsEnabled = true,
                OverridesJson = overridesJson,
            }
        );
    }

    private async Task SaveAndAnnounceAsync(
        Guid broadcaster,
        string builtinKey,
        string action,
        CancellationToken ct
    )
    {
        await _db.SaveChangesAsync(ct);
        await _registry.InvalidateBuiltinsAsync(broadcaster, ct);
        await _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcaster,
                Domain = "builtins",
                EntityId = builtinKey,
                Action = action,
            },
            ct
        );
    }

    private static BuiltinCommandDto ToDto(
        IBuiltinCommand command,
        IReadOnlyList<ChannelBuiltinCommand> rows
    )
    {
        string key = Normalize(command.BuiltinKey);
        string replyGroup = BuiltinResponseSlots.ReplyGroupFor(key);
        ChannelBuiltinCommand? own = rows.FirstOrDefault(r => Normalize(r.BuiltinKey) == key);
        BuiltinOverrides overrides = BuiltinOverridesJson.Read(key, own?.OverridesJson);

        // Replies live on the reply group's rows (a legacy sibling row may still hold some): count distinct slots.
        int replyOverrideCount = rows.Where(r =>
                BuiltinResponseSlots.ReplyGroupFor(r.BuiltinKey) == replyGroup
            )
            .SelectMany(r =>
                BuiltinOverridesJson.Read(r.BuiltinKey, r.OverridesJson).Responses.Keys
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        // A reserved built-in (gdpr-crypto.md §9) is always on with fixed settings — stray values are ignored.
        bool reserved = command.IsReserved;
        return new BuiltinCommandDto(
            command.BuiltinKey,
            "!" + command.BuiltinKey,
            reserved || own is null || own.IsEnabled,
            command.DefaultCooldownSeconds,
            PermissionLevelNames.ToName(command.DefaultMinPermissionLevel),
            replyGroup,
            !reserved && overrides.SpeakWithTts,
            reserved,
            reserved ? null : overrides.CooldownSeconds,
            reserved || overrides.MinPermissionLevel is not { } floor
                ? null
                : PermissionLevelNames.ToName(Math.Max(floor, command.DefaultMinPermissionLevel)),
            replyOverrideCount
        );
    }

    private static string Normalize(string builtinKey) =>
        builtinKey.TrimStart('!').ToLowerInvariant();
}
