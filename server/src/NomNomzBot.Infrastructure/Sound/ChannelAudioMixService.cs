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

        if (request.MusicDuckPercent is { } duck && !IsInRange(duck))
            return Result.Failure<ChannelAudioMixDto>(
                "audio_mix_music_duck_percent_out_of_range",
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
        row.MusicDuckPercent = request.MusicDuckPercent ?? row.MusicDuckPercent;
        await db.SaveChangesAsync(ct);
        return Result.Success(ToDto(row));
    }

    private static bool IsInRange(int volume) =>
        volume is >= AudioMixDefaults.MinVolume and <= AudioMixDefaults.MaxVolume;

    private static ChannelAudioMixDto ToDto(ChannelAudioMix row) =>
        new(row.MasterVolume, row.TtsVolume, row.MusicDuckPercent);
}
