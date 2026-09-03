// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Commands;

public sealed class BuiltinCommandService : IBuiltinCommandService
{
    private static readonly JsonSerializerOptions OverridesJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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

        List<BuiltinCommandDto> dtos =
        [
            .. _catalog
                .GetAll()
                .Select(cmd =>
                {
                    // A reserved built-in (gdpr-crypto.md §9) is always on — any stray toggle row is ignored.
                    bool isEnabled =
                        cmd.IsReserved
                        || !toggles.TryGetValue(cmd.BuiltinKey, out ChannelBuiltinCommand? toggle)
                        || toggle.IsEnabled;

                    string? responseOverride = toggles.TryGetValue(
                        cmd.BuiltinKey,
                        out ChannelBuiltinCommand? row
                    )
                        ? ParseResponseOverride(row.OverridesJson)
                        : null;

                    return new BuiltinCommandDto(
                        cmd.BuiltinKey,
                        "!" + cmd.BuiltinKey,
                        isEnabled,
                        cmd.DefaultCooldownSeconds,
                        PermissionLevelNames.ToName(cmd.DefaultMinPermissionLevel),
                        responseOverride
                    );
                }),
        ];

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

    public async Task<Result> SetResponseOverrideAsync(
        string broadcasterId,
        string builtinKey,
        string? template,
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
                $"'{builtinKey}' is a reserved data-rights command — its response cannot be overridden.",
                "VALIDATION_FAILED"
            );

        // Blank clears the override — the built-in falls back to the tone template, then its neutral string.
        string? normalized = string.IsNullOrWhiteSpace(template) ? null : template.Trim();
        string? overridesJson = normalized is null
            ? null
            : JsonSerializer.Serialize(
                new BuiltinOverridesPayload(normalized),
                OverridesJsonOptions
            );

        ChannelBuiltinCommand? existing = await _db.ChannelBuiltinCommands.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcaster && c.BuiltinKey == builtinKey,
            ct
        );

        if (existing is null)
        {
            if (overridesJson is null)
                return Result.Success(); // Nothing to clear — no row exists.

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
        else
        {
            existing.OverridesJson = overridesJson;
        }

        await _db.SaveChangesAsync(ct);
        await _registry.InvalidateBuiltinsAsync(broadcaster, ct);
        await _eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcaster,
                Domain = "builtins",
                EntityId = builtinKey,
                Action = "response_override_set",
            },
            ct
        );
        return Result.Success();
    }

    /// <summary>Mirrors the <c>{ "responseTemplate": "..." }</c> shape the channel registry parses.</summary>
    private static string? ParseResponseOverride(string? overridesJson)
    {
        if (string.IsNullOrWhiteSpace(overridesJson))
            return null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(overridesJson);
            if (
                doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("responseTemplate", out JsonElement value)
                && value.ValueKind == JsonValueKind.String
            )
            {
                string? parsed = value.GetString();
                return string.IsNullOrWhiteSpace(parsed) ? null : parsed;
            }
        }
        catch (JsonException)
        {
            // Malformed override — treat as absent, same tolerance as the channel registry loader.
        }

        return null;
    }

    private sealed record BuiltinOverridesPayload(string ResponseTemplate);
}
