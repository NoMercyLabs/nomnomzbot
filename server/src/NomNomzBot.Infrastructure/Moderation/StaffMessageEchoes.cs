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
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// Tells whether a viewer's message repeats something the broadcaster or a moderator said in the same
/// channel moments ago. A viewer who pastes the streamer's own snippet is quoting, not spamming.
/// </summary>
public sealed class StaffMessageEchoes
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private const int MaxStaffMessages = 200;

    private readonly IApplicationDbContext _db;
    private readonly TimeProvider _time;

    public StaffMessageEchoes(IApplicationDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<bool> IsEchoAsync(Guid broadcasterId, string skeleton, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(skeleton))
            return false;

        DateTime since = _time.GetUtcNow().UtcDateTime - Window;
        List<string> staffMessages = await _db
            .ChatMessages.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m =>
                m.BroadcasterId == broadcasterId
                && m.DeletedAt == null
                && m.CreatedAt >= since
                && (m.UserType == "broadcaster" || m.UserType == "moderator")
            )
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => m.Message)
            .Take(MaxStaffMessages)
            .ToListAsync(ct);

        return staffMessages.Any(text => MessageNormalizer.Normalize(text).Skeleton == skeleton);
    }
}
