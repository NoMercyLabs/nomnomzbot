// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Dtos;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat;

/// <inheritdoc />
public sealed partial class ChatIdentityService(
    IApplicationDbContext db,
    ITwitchHelixClient helix,
    ITwitchTokenResolver tokens,
    IChatBadgeResolver badges
) : IChatIdentityService
{
    private static readonly IReadOnlyList<ChatNamedColorDto> NamedColors =
    [
        .. ChatColorPalette.Names.Select(
            (name, i) => new ChatNamedColorDto(name, ChatColorPalette.Colors[i])
        ),
    ];

    public async Task<Result<ChatIdentityDto>> GetAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        Result<TwitchUserChatColor> color = await helix.Chat.GetUserChatColorAsync(
            broadcasterId,
            ct
        );
        if (color.IsFailure)
            return color.WithValue<ChatIdentityDto>(default!);

        bool canManage = await tokens.HasScopeAsync(
            broadcasterId,
            TwitchScopes.UserManageChatColor,
            ct
        );
        IReadOnlyList<ChatIdentityBadgeDto> worn = await WornBadgesAsync(
            broadcasterId,
            color.Value.UserId,
            ct
        );

        return Result.Success(
            new ChatIdentityDto(
                color.Value.UserName,
                string.IsNullOrEmpty(color.Value.Color) ? null : color.Value.Color,
                canManage,
                TwitchScopes.UserManageChatColor,
                NamedColors,
                worn
            )
        );
    }

    public async Task<Result<ChatIdentityDto>> SetColorAsync(
        Guid broadcasterId,
        string color,
        CancellationToken ct = default
    )
    {
        string? value = Normalize(color);
        if (value is null)
            return Result.Failure<ChatIdentityDto>(
                "Pick one of the named colors or a hex value like #9146FF.",
                "VALIDATION_FAILED"
            );

        Result update = await helix.Chat.UpdateUserChatColorAsync(broadcasterId, value, ct);
        if (IsTurboRefusal(update))
            return Result.Failure<ChatIdentityDto>(
                "Twitch only lets Turbo or Prime members pick any hex color. Pick one of the named colors.",
                IChatIdentityService.HexNeedsTurboCode
            );

        if (update.IsFailure)
            return update.WithValue<ChatIdentityDto>(default!);

        return await GetAsync(broadcasterId, ct);
    }

    private async Task<IReadOnlyList<ChatIdentityBadgeDto>> WornBadgesAsync(
        Guid broadcasterId,
        string twitchUserId,
        CancellationToken ct
    )
    {
        List<ChatBadge>? latest = await db
            .ChatMessages.Where(m => m.BroadcasterId == broadcasterId && m.UserId == twitchUserId)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.Badges)
            .FirstOrDefaultAsync(ct);
        if (latest is null || latest.Count == 0)
            return [];

        IReadOnlyList<ResolvedChatBadge> resolved = await badges.ResolveAsync(
            broadcasterId,
            latest,
            ct
        );
        return [.. resolved.Select(b => new ChatIdentityBadgeDto(b.SetId, b.Id, b.Info, b.Urls))];
    }

    private static string? Normalize(string color)
    {
        string trimmed = color.Trim();
        string lower = trimmed.ToLowerInvariant();
        if (ChatColorPalette.Names.Contains(lower))
            return lower;

        return HexColor().IsMatch(trimmed) ? trimmed.ToUpperInvariant() : null;
    }

    private static bool IsTurboRefusal(Result update) =>
        update.IsFailure
        && update.ErrorCode == TwitchErrorCodes.TwitchError
        && (update.ErrorDetail?.Contains("Turbo", StringComparison.OrdinalIgnoreCase) ?? false);

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();
}
