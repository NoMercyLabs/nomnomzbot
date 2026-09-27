// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.PlatformDefaults.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.PlatformDefaults;

/// <summary>
/// Counts who feels a platform-default change: the ACTIVE channels that do not carry their own setting for it.
/// Every family supplies the ids of the channels whose own setting keeps winning; the rest follow the default.
/// </summary>
internal static class PlatformDefaultBlastRadius
{
    private const int SampleSize = 5;

    public static Task<PlatformDefaultBlastRadiusDto> CountAsync(
        IApplicationDbContext db,
        IQueryable<Guid> channelsWithOwnSetting,
        bool valueChanges,
        bool requiresDangerConfirmation,
        CancellationToken ct
    ) =>
        CountAsync(
            db,
            c => channelsWithOwnSetting.Contains(c.Id),
            c => !channelsWithOwnSetting.Contains(c.Id),
            valueChanges,
            requiresDangerConfirmation,
            ct
        );

    /// <summary>Same count, for a family that resolves its own-setting channels in memory.</summary>
    public static Task<PlatformDefaultBlastRadiusDto> CountAsync(
        IApplicationDbContext db,
        IReadOnlyCollection<Guid> channelsWithOwnSetting,
        bool valueChanges,
        bool requiresDangerConfirmation,
        CancellationToken ct
    ) =>
        CountAsync(
            db,
            c => channelsWithOwnSetting.Contains(c.Id),
            c => !channelsWithOwnSetting.Contains(c.Id),
            valueChanges,
            requiresDangerConfirmation,
            ct
        );

    private static async Task<PlatformDefaultBlastRadiusDto> CountAsync(
        IApplicationDbContext db,
        Expression<Func<Channel, bool>> keepsOwnSetting,
        Expression<Func<Channel, bool>> followsDefault,
        bool valueChanges,
        bool requiresDangerConfirmation,
        CancellationToken ct
    )
    {
        IQueryable<Channel> active = db.Channels.Where(c =>
            c.Status == AuthEnums.ChannelStatus.Active
        );
        IQueryable<Channel> following = active.Where(followsDefault);

        int keeping = await active.CountAsync(keepsOwnSetting, ct);
        if (!valueChanges)
            return new(0, keeping, [], requiresDangerConfirmation);

        int affected = await following.CountAsync(ct);
        List<string> sample = await following
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .Take(SampleSize)
            .ToListAsync(ct);
        return new(affected, keeping, sample, requiresDangerConfirmation);
    }
}
