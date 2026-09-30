// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Infrastructure.Sound.Audio;

namespace NomNomzBot.Infrastructure.Tests.Sound;

/// <summary>
/// The one sound clip file rule set: format by content sniff, a size cap, and a duration cap that holds for
/// every accepted format — including Ogg, whose length is read from the last page's granule position.
/// </summary>
public sealed class SoundClipAudioRulesTests
{
    [Fact]
    public async Task A_short_wav_passes_with_its_probed_duration_and_the_whole_file_buffered()
    {
        byte[] wav = TestAudio.Wav(seconds: 2);

        Result<ValidatedAudio> result = await SoundClipAudioRules.ValidateAsync(
            new MemoryStream(wav),
            CancellationToken.None
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        await using ValidatedAudio audio = result.Value;
        audio.MimeType.Should().Be("audio/wav");
        audio.DurationMs.Should().Be(2_000);
        audio.Content.Position.Should().Be(0);
        audio.Content.ToArray().Should().Equal(wav);
    }

    [Fact]
    public async Task A_wav_over_three_minutes_is_refused_as_too_long()
    {
        Result<ValidatedAudio> result = await SoundClipAudioRules.ValidateAsync(
            new MemoryStream(TestAudio.Wav(seconds: 181)),
            CancellationToken.None
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("DURATION_EXCEEDED");
        result.ErrorMessage.Should().Contain("3 minute");
    }

    [Fact]
    public async Task An_ogg_vorbis_length_comes_from_the_last_granule_and_the_cap_applies()
    {
        Result<ValidatedAudio> shortOgg = await SoundClipAudioRules.ValidateAsync(
            new MemoryStream(TestAudio.OggVorbis(seconds: 5)),
            CancellationToken.None
        );
        Result<ValidatedAudio> longOgg = await SoundClipAudioRules.ValidateAsync(
            new MemoryStream(TestAudio.OggVorbis(seconds: 200)),
            CancellationToken.None
        );

        shortOgg.IsSuccess.Should().BeTrue(shortOgg.ErrorMessage);
        shortOgg.Value.MimeType.Should().Be("audio/ogg");
        shortOgg.Value.DurationMs.Should().Be(5_000);
        longOgg.ErrorCode.Should().Be("DURATION_EXCEEDED");
    }

    [Fact]
    public async Task Content_that_is_not_audio_is_refused_whatever_it_claims_to_be()
    {
        Result<ValidatedAudio> result = await SoundClipAudioRules.ValidateAsync(
            new MemoryStream(TestAudio.NotAudio()),
            CancellationToken.None
        );

        result.ErrorCode.Should().Be("INVALID_FORMAT");
    }
}
