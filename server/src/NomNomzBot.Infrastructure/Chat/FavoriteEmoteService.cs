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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.Entities;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>Stores the signed-in user's favorite emotes (see <see cref="IFavoriteEmoteService"/>).</summary>
public sealed class FavoriteEmoteService(IApplicationDbContext db) : IFavoriteEmoteService
{
    private const string ValidationFailed = "VALIDATION_FAILED";

    public async Task<Result<IReadOnlyList<FavoriteEmoteDto>>> ListAsync(
        Guid userId,
        CancellationToken ct = default
    )
    {
        List<FavoriteEmote> rows = await db
            .FavoriteEmotes.AsNoTracking()
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .ThenByDescending(f => f.Id)
            .ToListAsync(ct);

        return Result.Success<IReadOnlyList<FavoriteEmoteDto>>([.. rows.Select(ToDto)]);
    }

    public async Task<Result<FavoriteEmoteDto>> AddAsync(
        Guid userId,
        SetFavoriteEmoteRequest request,
        CancellationToken ct = default
    )
    {
        string? invalid = Validate(request);
        if (invalid is not null)
            return Result.Failure<FavoriteEmoteDto>(invalid, ValidationFailed);

        FavoriteEmote? existing = await db.FavoriteEmotes.FirstOrDefaultAsync(
            f =>
                f.UserId == userId
                && f.Provider == request.Provider
                && f.EmoteId == request.EmoteId,
            ct
        );
        if (existing is not null)
        {
            existing.Code = request.Code;
            existing.ImageUrl = request.ImageUrl;
            await db.SaveChangesAsync(ct);
            return Result.Success(ToDto(existing));
        }

        int count = await db.FavoriteEmotes.CountAsync(f => f.UserId == userId, ct);
        if (count >= FavoriteEmoteLimits.MaxPerUser)
            return Result.Failure<FavoriteEmoteDto>(
                $"You can keep at most {FavoriteEmoteLimits.MaxPerUser} favorite emotes. Remove one to add another.",
                ValidationFailed
            );

        FavoriteEmote row = new()
        {
            UserId = userId,
            Provider = request.Provider,
            EmoteId = request.EmoteId,
            Code = request.Code,
            ImageUrl = request.ImageUrl,
        };
        db.FavoriteEmotes.Add(row);
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(row));
    }

    public async Task<Result> RemoveAsync(
        Guid userId,
        string provider,
        string emoteId,
        CancellationToken ct = default
    )
    {
        await db
            .FavoriteEmotes.Where(f =>
                f.UserId == userId && f.Provider == provider && f.EmoteId == emoteId
            )
            .ExecuteDeleteAsync(ct);
        return Result.Success();
    }

    private static string? Validate(SetFavoriteEmoteRequest request)
    {
        if (!FavoriteEmoteLimits.Providers.Contains(request.Provider))
            return $"Provider must be one of: {string.Join(", ", FavoriteEmoteLimits.Providers)}.";
        if (string.IsNullOrWhiteSpace(request.EmoteId) || request.EmoteId.Length > 100)
            return "EmoteId is required and at most 100 characters.";
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Length > 100)
            return "Code is required and at most 100 characters.";
        if (request.ImageUrl is { Length: > 500 })
            return "ImageUrl is at most 500 characters.";
        return null;
    }

    private static FavoriteEmoteDto ToDto(FavoriteEmote row) =>
        new(row.Provider, row.EmoteId, row.Code, row.ImageUrl, row.CreatedAt);
}
