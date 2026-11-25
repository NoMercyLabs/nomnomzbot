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

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Widgets;

internal sealed partial class OverlayThemeColorService(IApplicationDbContext db)
    : IOverlayThemeColorService
{
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();

    public async Task<Result<OverlayThemeColor>> GetAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        string? stored = await db
            .Configurations.Where(c =>
                c.BroadcasterId == broadcasterId && c.Key == IOverlayThemeColorService.ConfigKey
            )
            .Select(c => c.Value)
            .FirstOrDefaultAsync(ct);
        return Result.Success(new OverlayThemeColor(stored?.Trim() ?? string.Empty));
    }

    public async Task<Result<OverlayThemeColor>> SetAsync(
        Guid broadcasterId,
        UpdateOverlayThemeColorRequest request,
        CancellationToken ct = default
    )
    {
        string color = request.Color?.Trim() ?? string.Empty;
        if (color.Length > 0 && !HexColor().IsMatch(color))
            return Result.Failure<OverlayThemeColor>(
                "overlay_theme_color_invalid",
                errorCode: "VALIDATION_FAILED"
            );

        Configuration? row = await db.Configurations.FirstOrDefaultAsync(
            c => c.BroadcasterId == broadcasterId && c.Key == IOverlayThemeColorService.ConfigKey,
            ct
        );
        if (color.Length == 0)
        {
            if (row is not null)
                db.Configurations.Remove(row);
        }
        else if (row is null)
            db.Configurations.Add(
                new()
                {
                    BroadcasterId = broadcasterId,
                    Key = IOverlayThemeColorService.ConfigKey,
                    Value = color,
                }
            );
        else
            row.Value = color;

        await db.SaveChangesAsync(ct);
        return Result.Success(new OverlayThemeColor(color));
    }
}
