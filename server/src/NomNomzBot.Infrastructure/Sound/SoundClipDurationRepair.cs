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
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Domain.Sound.Entities;
using NomNomzBot.Infrastructure.Sound.Audio;

namespace NomNomzBot.Infrastructure.Sound;

/// <summary>What one run of <see cref="SoundClipDurationRepair"/> did to the stored MP3 clips.</summary>
public sealed record SoundClipDurationRepairReport(int Changed, int Unchanged, int Skipped);

/// <summary>
/// Measures every stored MP3 clip again from its own bytes and corrects its stored length. Clips stored before
/// the MP3 probe returned milliseconds carry a length 1000 times too short, so a wait-for-finish sound step on
/// them returned at once. A clip whose bytes cannot be read, or whose probe finds no length, keeps its stored
/// value: nothing is guessed and 0 is never written. Safe to run twice: a clip already right is not written.
/// </summary>
public sealed class SoundClipDurationRepair(
    IApplicationDbContext db,
    ISoundClipStore store,
    ILogger<SoundClipDurationRepair> logger
)
{
    private const string Mp3MimeType = "audio/mpeg";

    private enum Outcome
    {
        Changed,
        Unchanged,
        Skipped,
    }

    public async Task<SoundClipDurationRepairReport> RunAsync(CancellationToken ct = default)
    {
        List<SoundClip> clips = await db
            .SoundClips.IgnoreQueryFilters()
            .Where(c => c.DeletedAt == null && c.MimeType == Mp3MimeType)
            .ToListAsync(ct);

        int changed = 0;
        int unchanged = 0;
        int skipped = 0;
        foreach (SoundClip clip in clips)
        {
            switch (await RepairOneAsync(clip, ct))
            {
                case Outcome.Changed:
                    changed++;
                    break;
                case Outcome.Unchanged:
                    unchanged++;
                    break;
                default:
                    skipped++;
                    break;
            }
        }

        logger.LogInformation(
            "Sound clip length repair: {Changed} changed, {Unchanged} unchanged, {Skipped} skipped",
            changed,
            unchanged,
            skipped
        );
        return new(changed, unchanged, skipped);
    }

    private async Task<Outcome> RepairOneAsync(SoundClip clip, CancellationToken ct)
    {
        int storedDurationMs = clip.DurationMs;
        try
        {
            Result<System.IO.Stream> opened = await store.OpenAsync(clip.StorageKey, ct);
            if (opened.IsFailure)
                return Skip(clip, "its file could not be read");

            int probedMs;
            await using (System.IO.Stream source = opened.Value)
            {
                using MemoryStream bytes = new();
                await source.CopyToAsync(bytes, ct);
                probedMs = AudioSniffer.ProbeDurationMs(bytes, Mp3MimeType);
            }

            if (probedMs <= 0)
                return Skip(clip, "its length could not be measured");
            if (probedMs == storedDurationMs)
                return Outcome.Unchanged;

            clip.DurationMs = probedMs;
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Sound clip {ClipId} of channel {ChannelId}: length {OldMs} ms -> {NewMs} ms",
                clip.Id,
                clip.BroadcasterId,
                storedDurationMs,
                probedMs
            );
            return Outcome.Changed;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            clip.DurationMs = storedDurationMs;
            logger.LogError(ex, "Sound clip {ClipId} length repair failed", clip.Id);
            return Outcome.Skipped;
        }
    }

    private Outcome Skip(SoundClip clip, string reason)
    {
        logger.LogWarning(
            "Sound clip {ClipId} of channel {ChannelId} skipped: {Reason}",
            clip.Id,
            clip.BroadcasterId,
            reason
        );
        return Outcome.Skipped;
    }
}
