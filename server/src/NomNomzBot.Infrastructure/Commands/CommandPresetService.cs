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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands.Presets;

namespace NomNomzBot.Infrastructure.Commands;

/// <summary>
/// <see cref="ICommandPresetService"/>. Seeding goes through <see cref="ICommandService.CreateAsync"/> — the
/// same path the dashboard's "new command" form uses — and then marks the row with its
/// <see cref="Command.PresetKey"/>, which is what later makes it resettable.
/// </summary>
public sealed class CommandPresetService(
    ICommandService commands,
    IApplicationDbContext db,
    IChannelRegistry registry,
    IEventBus eventBus,
    ITemplateHelperValidator templateHelperValidator
) : ICommandPresetService
{
    public IReadOnlyList<CommandPresetDto> ListPresets() =>
        [.. FunCommandPresets.All.Select(ToDto)];

    public async Task<Result<CommandPresetSeedReport>> SeedAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<string> names = [.. FunCommandPresets.All.Select(p => p.Name)];

        // Read past the soft-delete filter on purpose: a deleted preset is a tombstone, and the tombstone is
        // the channel's decision that the command must stay gone. The tenant filter is bypassed too because
        // this runs from the startup backfill for every channel; the channel is named explicitly instead.
        List<Command> rows = await db
            .Commands.IgnoreQueryFilters()
            .Where(c => c.BroadcasterId == broadcasterId && names.Contains(c.NameNormalized))
            .ToListAsync(cancellationToken);

        int seeded = 0;
        int alreadyPresent = 0;
        int deletedByChannel = 0;
        List<string> failed = [];

        foreach (CreateCommandDto preset in FunCommandPresets.All)
        {
            Command? live = rows.FirstOrDefault(r =>
                r.NameNormalized == preset.Name && r.DeletedAt == null
            );
            if (live is not null)
            {
                AdoptIfSeededFrom(live, preset);
                alreadyPresent++;
                continue;
            }

            if (rows.Any(r => r.NameNormalized == preset.Name && r.DeletedAt != null))
            {
                deletedByChannel++;
                continue;
            }

            Result<CommandDto> created = await commands.CreateAsync(
                broadcasterId.ToString(),
                preset,
                cancellationToken
            );
            if (created.IsSuccess)
            {
                await MarkAsPresetAsync(created.Value.Id, preset.Name, cancellationToken);
                seeded++;
            }
            else if (created.ErrorCode == "ALREADY_EXISTS")
                alreadyPresent++;
            else
                failed.Add($"{preset.Name}: {created.ErrorMessage} ({created.ErrorCode})");
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(
            new CommandPresetSeedReport(seeded, alreadyPresent, deletedByChannel, failed)
        );
    }

    public async Task<Result<CommandDto>> ResetAsync(
        string broadcasterId,
        string commandName,
        CancellationToken cancellationToken = default
    )
    {
        if (!Guid.TryParse(broadcasterId, out Guid broadcaster))
            return Errors
                .ValidationFailed($"Invalid channel ID '{broadcasterId}'.")
                .ToTyped<CommandDto>();

        string nameNormalized = commandName.ToLowerInvariant();
        Command? command = await db.Commands.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcaster && c.NameNormalized == nameNormalized,
            cancellationToken
        );
        if (command is null)
            return Errors.NotFound<CommandDto>("Command", commandName);

        CreateCommandDto? preset = command.PresetKey is { } key
            ? FunCommandPresets.Find(key)
            : null;
        if (preset is null)
            return Result.Failure<CommandDto>(
                $"'{command.Name}' was written by the channel, not seeded from a preset, so there is nothing to reset it to.",
                "NOT_A_PRESET"
            );

        Result helpersValid = ValidateHelpers(preset);
        if (helpersValid.IsFailure)
            return helpersValid.ToTyped<CommandDto>();

        ApplyPreset(command, preset);
        await db.SaveChangesAsync(cancellationToken);
        await registry.InvalidateCommandsAsync(broadcaster, cancellationToken);
        await eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcaster,
                Domain = "commands",
                EntityId = command.Id.ToString(),
                Action = "updated",
            },
            cancellationToken
        );

        return await commands.GetAsync(broadcasterId, command.Name, cancellationToken);
    }

    /// <summary>
    /// A reset writes the preset's responses straight onto the row, so they pass the same save-time helper
    /// check as a response the streamer types in.
    /// </summary>
    private Result ValidateHelpers(CreateCommandDto preset)
    {
        foreach (
            string? response in (preset.TemplateResponses ?? []).Prepend(preset.TemplateResponse)
        )
        {
            Result result = templateHelperValidator.Validate(
                response,
                TemplateHelperContext.Command
            );
            if (result.IsFailure)
                return result;
        }

        return Result.Success();
    }

    /// <summary>
    /// Everything the preset defines, back onto the row. The name and the on/off state stay: the streamer may
    /// have renamed the command or switched it off on purpose, and a reset must not undo either.
    /// </summary>
    private static void ApplyPreset(Command command, CreateCommandDto preset)
    {
        command.Tier = preset.Tier;
        command.Description = preset.Description;
        command.TemplateResponse = preset.TemplateResponse;
        command.TemplateResponses = [.. preset.TemplateResponses ?? []];
        command.PipelineId = null;
        command.MinPermissionLevel =
            PermissionLevelNames.ToLevelValue(preset.MinPermissionLevel) ?? 0;
        command.CooldownSeconds = preset.CooldownSeconds;
        command.UserCooldownSeconds = preset.UserCooldownSeconds;
        command.CooldownPerUser = preset.CooldownPerUser;
        command.Aliases = [.. preset.Aliases ?? []];
        command.PrefixMode = preset.PrefixMode;
        command.CustomPrefix = null;
        command.MatchMode = preset.MatchMode;
        command.MatchPattern = null;
    }

    /// <summary>
    /// Rows seeded before <see cref="Command.PresetKey"/> existed carry no mark. One is adopted only when it
    /// still carries a piece of the preset (its description or its responses) — a same-named command the
    /// streamer wrote themselves is never taken for the preset on its name alone.
    /// </summary>
    private static void AdoptIfSeededFrom(Command live, CreateCommandDto preset)
    {
        if (live.PresetKey is not null)
            return;

        bool sameDescription = live.Description == preset.Description;
        bool sameResponses =
            live.TemplateResponse == preset.TemplateResponse
            && (live.TemplateResponses ?? []).SequenceEqual(preset.TemplateResponses ?? []);
        if (sameDescription || sameResponses)
            live.PresetKey = preset.Name;
    }

    private async Task MarkAsPresetAsync(Guid commandId, string key, CancellationToken ct)
    {
        Command? created = await db
            .Commands.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == commandId, ct);
        if (created is not null)
            created.PresetKey = key;
    }

    private static CommandPresetDto ToDto(CreateCommandDto preset) =>
        new(
            preset.Name,
            preset.Tier,
            preset.Description,
            preset.TemplateResponse,
            [.. preset.TemplateResponses ?? []],
            preset.MinPermissionLevel,
            preset.CooldownSeconds,
            preset.UserCooldownSeconds,
            preset.CooldownPerUser,
            [.. preset.Aliases ?? []],
            preset.PrefixMode,
            preset.MatchMode
        );
}
