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
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Domain.Sound.Entities;

namespace NomNomzBot.Infrastructure.Sound;

internal sealed class ChannelAudioMixService(IApplicationDbContext db) : IChannelAudioMixService
{
    public async Task<Result<ChannelAudioMixDto>> GetAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        ChannelAudioMix? row = await db.ChannelAudioMixes.FirstOrDefaultAsync(
            m => m.BroadcasterId == broadcasterId,
            ct
        );
        return Result.Success(
            row is null
                ? new ChannelAudioMixDto(AudioMixDefaults.MasterVolume, AudioMixDefaults.TtsVolume)
                : ToDto(row)
        );
    }

    public async Task<Result<ChannelAudioMixDto>> UpdateAsync(
        Guid broadcasterId,
        UpdateChannelAudioMixRequest request,
        CancellationToken ct = default
    )
    {
        if (!IsInRange(request.MasterVolume))
            return Result.Failure<ChannelAudioMixDto>(
                "audio_mix_master_volume_out_of_range",
                errorCode: "VALIDATION_FAILED"
            );
        if (!IsInRange(request.TtsVolume))
            return Result.Failure<ChannelAudioMixDto>(
                "audio_mix_tts_volume_out_of_range",
                errorCode: "VALIDATION_FAILED"
            );

        ChannelAudioMix? row = await db.ChannelAudioMixes.FirstOrDefaultAsync(
            m => m.BroadcasterId == broadcasterId,
            ct
        );
        if (row is null)
        {
            row = new() { BroadcasterId = broadcasterId };
            db.ChannelAudioMixes.Add(row);
        }
        row.MasterVolume = request.MasterVolume;
        row.TtsVolume = request.TtsVolume;
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(row));
    }

    private static bool IsInRange(int volume) =>
        volume is >= AudioMixDefaults.MinVolume and <= AudioMixDefaults.MaxVolume;

    private static ChannelAudioMixDto ToDto(ChannelAudioMix row) =>
        new(row.MasterVolume, row.TtsVolume);
}
