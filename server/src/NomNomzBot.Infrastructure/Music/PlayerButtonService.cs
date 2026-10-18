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
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using ChannelConfiguration = NomNomzBot.Domain.Platform.Entities.Configuration;

namespace NomNomzBot.Infrastructure.Music;

public sealed partial class PlayerButtonService(IApplicationDbContext db, IEventBus eventBus)
    : IPlayerButtonService
{
    private const string ConfigKey = "music:player-buttons";

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex IconKeyPattern();

    public async Task<Result<IReadOnlyList<PlayerButtonDto>>> GetAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default
    )
    {
        ChannelConfiguration? row = await db
            .Configurations.AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.BroadcasterId == tenantId && c.Key == ConfigKey,
                cancellationToken
            );
        return Result.Success<IReadOnlyList<PlayerButtonDto>>(Parse(row?.Value));
    }

    public async Task<Result<IReadOnlyList<PlayerButtonDto>>> ReplaceAsync(
        Guid tenantId,
        IReadOnlyList<PlayerButtonDto> buttons,
        CancellationToken cancellationToken = default
    )
    {
        string? problem = FirstProblem(buttons);
        if (problem is not null)
            return Result.Failure<IReadOnlyList<PlayerButtonDto>>(problem, "VALIDATION_FAILED");

        List<PlayerButtonDto> stored =
        [
            .. buttons.Select(b =>
                b with
                {
                    Id = string.IsNullOrWhiteSpace(b.Id) ? Guid.NewGuid().ToString("N") : b.Id,
                    Label = b.Label.Trim(),
                    ActionType = b.ActionType.Trim(),
                    Parameters = b.Parameters ?? [],
                }
            ),
        ];
        string json = JsonSerializer.Serialize(stored);

        ChannelConfiguration? existing = await db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == tenantId && c.Key == ConfigKey,
            cancellationToken
        );
        if (existing is not null)
            existing.Value = json;
        else
            db.Configurations.Add(
                new()
                {
                    BroadcasterId = tenantId,
                    Key = ConfigKey,
                    Value = json,
                }
            );
        await db.SaveChangesAsync(cancellationToken);

        await eventBus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = tenantId,
                Domain = "music-player-buttons",
                Action = "updated",
            },
            cancellationToken
        );
        return Result.Success<IReadOnlyList<PlayerButtonDto>>(stored);
    }

    private static string? FirstProblem(IReadOnlyList<PlayerButtonDto> buttons)
    {
        if (buttons.Count > PlayerButtonRules.MaxButtons)
            return $"A channel can pin at most {PlayerButtonRules.MaxButtons} player buttons.";

        for (int i = 0; i < buttons.Count; i++)
        {
            PlayerButtonDto button = buttons[i];
            string label = button.Label?.Trim() ?? string.Empty;
            if (label.Length is 0 or > PlayerButtonRules.MaxLabelLength)
                return $"Button {i + 1}: the label must be 1 to {PlayerButtonRules.MaxLabelLength} characters.";
            if (string.IsNullOrEmpty(button.Icon) || !IconKeyPattern().IsMatch(button.Icon))
                return $"Button {i + 1}: the icon key may only hold lowercase letters, digits and hyphens.";
            if (string.IsNullOrWhiteSpace(button.ActionType))
                return $"Button {i + 1}: pick an action to run.";
        }

        return null;
    }

    private static List<PlayerButtonDto> Parse(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<PlayerButtonDto>>(json) ?? [];
}
