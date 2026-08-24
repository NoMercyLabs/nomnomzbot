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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Domain.Analytics.Entities;

namespace NomNomzBot.Infrastructure.Identity.Pipeline;

/// <summary>
/// Supplies the channel's known viewers for the <c>twitch_user</c> resource-picker kind (S-RICH-PICKERS),
/// sourced from the per-channel <see cref="ViewerProfile"/> aggregate (schema M.1) — everyone who has actually
/// been seen in this channel, not a channel-wide Twitch search (Helix has none). <see cref="PipelineOption.Value"/>
/// is the Twitch user id, matching what every <c>user_id</c> field (ban/shoutout/…) actually stores.
/// <see cref="PipelineOption.SecondaryText"/> is the login (<c>@username</c>), which frequently differs from the
/// display name. This source can grow large, so results are paginated and searchable.
/// </summary>
internal sealed class TwitchUserOptionProvider : IPipelineOptionProvider
{
    private readonly IApplicationDbContext _db;

    public TwitchUserOptionProvider(IApplicationDbContext db)
    {
        _db = db;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.TwitchUser;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        string? search,
        PaginationParams pagination,
        CancellationToken ct = default
    )
    {
        IQueryable<ViewerProfile> query = _db.ViewerProfiles.Where(p =>
            p.BroadcasterId == broadcasterId
        );
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.ToLowerInvariant();
            query = query.Where(p =>
                (p.DisplayNameSnapshot != null && p.DisplayNameSnapshot.ToLower().Contains(term))
                || (p.UsernameSnapshot != null && p.UsernameSnapshot.ToLower().Contains(term))
            );
        }

        int total = await query.CountAsync(ct);
        List<ViewerProfile> page = await query
            .OrderByDescending(p => p.LastSeenAt)
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(ct);

        Dictionary<Guid, string?> avatarsByUserId = await _db
            .Users.Where(u => page.Select(p => p.ViewerUserId).Contains(u.Id))
            .Select(u => new { u.Id, u.ProfileImageUrl })
            .ToDictionaryAsync(u => u.Id, u => u.ProfileImageUrl, ct);

        List<PipelineOption> options =
        [
            .. page.Select(p => ToOption(p, avatarsByUserId.GetValueOrDefault(p.ViewerUserId))),
        ];
        return Result.Success(PipelineOptionListResult.Of(options, total));
    }

    private static PipelineOption ToOption(ViewerProfile profile, string? avatarUrl)
    {
        string label =
            profile.DisplayNameSnapshot ?? profile.UsernameSnapshot ?? profile.ViewerTwitchUserId;
        string? secondaryText = profile.UsernameSnapshot is null
            ? null
            : $"@{profile.UsernameSnapshot}";

        return new PipelineOption(
            profile.ViewerTwitchUserId,
            label,
            secondaryText,
            avatarUrl,
            PipelineOptionState.Selectable
        );
    }
}
