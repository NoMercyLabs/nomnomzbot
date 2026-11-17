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

namespace NomNomzBot.Infrastructure.Music.SponsorBlock;

/// <summary>One community-marked off-topic span of a video, in seconds. Duration is 0 when unknown.</summary>
public sealed record SponsorBlockSegment(
    double StartSeconds,
    double EndSeconds,
    double VideoDurationSeconds
);

/// <summary>
/// The part of a video that is the music: the video minus an off-topic intro and outro. EndMs 0 means the
/// natural end. A middle segment is ignored: the overlay player plays one continuous span.
/// </summary>
public sealed record YouTubeMusicSection(int StartMs, int EndMs)
{
    // A marker this close to the video's edge counts as touching it.
    private const double EdgeToleranceSeconds = 1.5;

    // A section shorter than this is more likely a wrong mark than a song: the whole video plays.
    private const int MinimumSectionMs = 15_000;

    /// <summary>The section left after the intro and outro segments, or null when the whole video plays.</summary>
    public static YouTubeMusicSection? From(
        IReadOnlyList<SponsorBlockSegment> segments,
        int knownDurationMs
    )
    {
        double durationSeconds =
            knownDurationMs > 0
                ? knownDurationMs / 1000.0
                : segments.Select(s => s.VideoDurationSeconds).DefaultIfEmpty(0).Max();

        double musicStart = 0;
        foreach (SponsorBlockSegment segment in segments.OrderBy(s => s.StartSeconds))
        {
            if (segment.StartSeconds > musicStart + EdgeToleranceSeconds)
                break;
            musicStart = Math.Max(musicStart, segment.EndSeconds);
        }

        double musicEnd = durationSeconds;
        if (durationSeconds > 0)
        {
            foreach (SponsorBlockSegment segment in segments.OrderByDescending(s => s.EndSeconds))
            {
                if (segment.EndSeconds < musicEnd - EdgeToleranceSeconds)
                    break;
                musicEnd = Math.Min(musicEnd, segment.StartSeconds);
            }
        }

        int startMs = (int)Math.Round(musicStart * 1000);
        int endMs = musicEnd < durationSeconds ? (int)Math.Round(musicEnd * 1000) : 0;
        if (startMs == 0 && endMs == 0)
            return null;

        int playedMs = (endMs > 0 ? endMs : (int)Math.Round(durationSeconds * 1000)) - startMs;
        if (durationSeconds > 0 && playedMs < MinimumSectionMs)
            return null;

        return new(startMs, endMs);
    }
}
