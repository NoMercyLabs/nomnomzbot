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
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>See <see cref="IChannelNamePronunciationService"/>. Read straight from the channels table.</summary>
public sealed class ChannelNamePronunciationService : IChannelNamePronunciationService
{
    private readonly IApplicationDbContext _db;

    public ChannelNamePronunciationService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ChannelNamePronunciation>> ListAsync(
        CancellationToken cancellationToken = default
    ) =>
        await _db
            .Channels.Where(c => c.UsernamePronunciation != null && c.UsernamePronunciation != "")
            .OrderBy(c => c.NameNormalized)
            .Select(c => new ChannelNamePronunciation(c.Name, c.UsernamePronunciation!))
            .ToListAsync(cancellationToken);
}
