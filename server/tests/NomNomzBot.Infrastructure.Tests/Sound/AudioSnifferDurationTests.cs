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

using FluentAssertions;
using NomNomzBot.Infrastructure.Sound.Audio;

namespace NomNomzBot.Infrastructure.Tests.Sound;

/// <summary>
/// A sound step that waits for the clip to finish waits for <c>DurationMs</c>, so the probe must return
/// MILLISECONDS. An MP3 at a constant bitrate lasts bytes * 8 / bitrate seconds.
/// </summary>
public sealed class AudioSnifferDurationTests
{
    // MPEG-1 Layer 3, 128 kbps, 44.1 kHz frame header, then silence up to the wanted size.
    private static MemoryStream ConstantBitrateMp3(int totalBytes)
    {
        byte[] bytes = new byte[totalBytes];
        bytes[0] = 0xFF;
        bytes[1] = 0xFB;
        bytes[2] = 0x90;
        bytes[3] = 0x00;
        return new MemoryStream(bytes);
    }

    [Fact]
    public void A_128_kbps_mp3_of_160000_bytes_lasts_ten_seconds_in_milliseconds()
    {
        using MemoryStream stream = ConstantBitrateMp3(160_000);

        int durationMs = AudioSniffer.ProbeDurationMs(stream, "audio/mpeg");

        durationMs.Should().Be(10_000);
    }

    [Fact]
    public void A_clip_longer_than_the_limit_is_measured_as_longer_than_the_limit()
    {
        // 128 kbps for 4 minutes is 3,840,000 bytes; the limit is 3 minutes.
        using MemoryStream stream = ConstantBitrateMp3(3_840_000);

        int durationMs = AudioSniffer.ProbeDurationMs(stream, "audio/mpeg");

        durationMs.Should().BeGreaterThan(SoundClipAudioRules.MaxDurationMs);
    }
}
