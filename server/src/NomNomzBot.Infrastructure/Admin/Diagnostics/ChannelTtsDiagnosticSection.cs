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
using NomNomzBot.Application.Admin.Diagnostics;
using NomNomzBot.Application.Admin.Dtos;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Admin.Diagnostics;

/// <summary>The <c>tts</c> section: the channel's last 50 TTS clips, and how long synthesis took over the last
/// 24 hours (clip count, failed count, average and 95th percentile by nearest rank).</summary>
public sealed class ChannelTtsDiagnosticSection(IApplicationDbContext db, TimeProvider clock)
    : IChannelDiagnosticSection
{
    private const int ClipLimit = 50;
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    public string Key => "tts";

    public async Task<Result> ReadIntoAsync(
        ChannelDiagnosticRequest request,
        ChannelDiagnosticsDto diagnostics,
        CancellationToken cancellationToken = default
    )
    {
        List<ChannelDiagnosticTtsClipDto> clips = await db
            .TtsUsageRecords.AsNoTracking()
            .Where(r => r.BroadcasterId == request.BroadcasterId)
            .OrderByDescending(r => r.OccurredAt)
            .ThenByDescending(r => r.Id)
            .Take(ClipLimit)
            .Select(r => new ChannelDiagnosticTtsClipDto(
                r.OccurredAt,
                r.Provider,
                r.VoiceId,
                r.CharacterCount,
                r.SynthesisMs,
                r.Failed
            ))
            .ToListAsync(cancellationToken);

        DateTime since = clock.GetUtcNow().UtcDateTime - Window;
        List<WindowSample> samples = await db
            .TtsUsageRecords.AsNoTracking()
            .Where(r => r.BroadcasterId == request.BroadcasterId && r.OccurredAt >= since)
            .Select(r => new WindowSample(r.SynthesisMs, r.Failed))
            .ToListAsync(cancellationToken);

        diagnostics.Tts = new(clips, Summarize(samples));
        return Result.Success();
    }

    private static ChannelDiagnosticTtsWindowDto Summarize(List<WindowSample> samples)
    {
        List<int> measured = samples
            .Where(s => s.SynthesisMs is not null)
            .Select(s => s.SynthesisMs!.Value)
            .Order()
            .ToList();

        return new(
            samples.Count,
            samples.Count(s => s.Failed),
            measured.Count == 0 ? null : (int)Math.Round(measured.Average()),
            measured.Count == 0 ? null : measured[P95Index(measured.Count)]
        );
    }

    /// <summary>The zero-based index of the nearest-rank 95th percentile (ceil(0.95 * n) - 1) in a sorted list of
    /// <paramref name="count"/>, in whole numbers so no rounding error moves it.</summary>
    private static int P95Index(int count) => (count * 95 + 99) / 100 - 1;

    private sealed record WindowSample(int? SynthesisMs, bool Failed);
}
