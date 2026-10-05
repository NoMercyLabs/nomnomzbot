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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Identity.Entities;

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

    public async Task<Result<ChannelNamePronunciationDto>> GetAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    )
    {
        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.Id == channelId,
            cancellationToken
        );
        return channel is null
            ? Errors.ChannelNotFound<ChannelNamePronunciationDto>(channelId.ToString())
            : Result.Success(ToDto(channel));
    }

    public async Task<Result<ChannelNamePronunciationDto>> SetAsync(
        Guid channelId,
        string? pronunciation,
        CancellationToken cancellationToken = default
    )
    {
        string? value = string.IsNullOrWhiteSpace(pronunciation) ? null : pronunciation.Trim();
        if (
            value is not null
            && (
                value.Length > IChannelNamePronunciationService.MaxLength
                || value.Any(char.IsControl)
            )
        )
        {
            return Result.Failure<ChannelNamePronunciationDto>(
                $"The pronunciation must be at most {IChannelNamePronunciationService.MaxLength} characters with no control characters.",
                "VALIDATION_FAILED"
            );
        }

        Channel? channel = await _db.Channels.FirstOrDefaultAsync(
            c => c.Id == channelId,
            cancellationToken
        );
        if (channel is null)
            return Errors.ChannelNotFound<ChannelNamePronunciationDto>(channelId.ToString());

        channel.UsernamePronunciation = value;
        await _db.SaveChangesAsync(cancellationToken);
        return Result.Success(ToDto(channel));
    }

    private static ChannelNamePronunciationDto ToDto(Channel channel) =>
        new(channel.Name, channel.UsernamePronunciation);
}
