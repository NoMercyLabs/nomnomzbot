// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Domain.Tts.Interfaces;
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves a spoken line's length comes from the audio it returned: real MP3 frame bytes give an exact duration
/// (the old estimate assumed 128 kbit/s and read about 37% of the real length for the 48 kbit/s the Edge and
/// Azure requests use). Edge reads its audio from a WebSocket, so a test cannot feed it bytes; it shares the
/// same reader call as Azure.
/// </summary>
public sealed class Mp3DurationTests
{
    // MPEG-2 Layer III, no CRC, 48 kbit/s, 24 kHz, mono: 576 samples = 24 ms, 72 * 48000 / 24000 = 144 bytes.
    private static byte[] Mpeg2Frames48Kbps24Khz(int count) =>
        Frames(count, [0xFF, 0xF3, 0x64, 0xC0], 144);

    // MPEG-1 Layer III, no CRC, 128 kbit/s, 44.1 kHz: 1152 samples, 144 * 128000 / 44100 = 417 bytes.
    private static byte[] Mpeg1Frames128Kbps44Khz(int count) =>
        Frames(count, [0xFF, 0xFB, 0x90, 0xC0], 417);

    private static byte[] Frames(int count, byte[] header, int frameLength)
    {
        byte[] all = new byte[count * frameLength];
        for (int i = 0; i < count; i++)
            header.CopyTo(all, i * frameLength);
        return all;
    }

    [Fact]
    public void Mpeg2_48kbps_24khz_frames_give_the_exact_duration()
    {
        Mp3Duration.ToMilliseconds(Mpeg2Frames48Kbps24Khz(100), 48).Should().Be(2400);
    }

    [Fact]
    public void Mpeg1_128kbps_44khz_frames_give_the_exact_duration()
    {
        // 100 * 1152 / 44100 s = 2612.2 ms.
        Mp3Duration.ToMilliseconds(Mpeg1Frames128Kbps44Khz(100), 128).Should().Be(2612);
    }

    [Fact]
    public void An_id3v2_tag_in_front_is_skipped()
    {
        // 10-byte header, syncsafe size 200 (0x00 0x00 0x01 0x48), 200 bytes of tag body.
        byte[] tag = new byte[210];
        tag[0] = (byte)'I';
        tag[1] = (byte)'D';
        tag[2] = (byte)'3';
        tag[3] = 4;
        tag[8] = 0x01;
        tag[9] = 0x48;
        byte[] audio = [.. tag, .. Mpeg2Frames48Kbps24Khz(50)];

        Mp3Duration.ToMilliseconds(audio, 48).Should().Be(1200);
    }

    [Fact]
    public void A_cut_off_last_frame_is_not_counted()
    {
        byte[] audio = [.. Mpeg2Frames48Kbps24Khz(10), 0xFF, 0xF3, 0x64, 0xC0, 0, 0, 0];

        Mp3Duration.ToMilliseconds(audio, 48).Should().Be(240);
    }

    [Fact]
    public void Bytes_without_a_frame_fall_back_to_the_requested_bitrate()
    {
        // 4800 bytes * 8 bits / 48000 bit/s = 0.8 s.
        Mp3Duration.ToMilliseconds(new byte[4800], 48).Should().Be(800);
    }

    private sealed class BytesHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
            );
    }

    private sealed class FakeFactory(byte[] body) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new BytesHandler(body));
    }

    [Fact]
    public async Task Azure_duration_comes_from_the_returned_audio()
    {
        byte[] audio = Mpeg2Frames48Kbps24Khz(50);
        AzureTtsProvider provider = new(
            new FakeFactory(audio),
            NullLogger<AzureTtsProvider>.Instance,
            "key"
        );

        TtsSynthesisResult result = await provider.SynthesizeAsync("hello", "en-US-JennyNeural");

        result.AudioData.Should().Equal(audio);
        result.DurationMs.Should().Be(1200);
    }

    [Fact]
    public async Task ElevenLabs_duration_comes_from_the_returned_audio()
    {
        byte[] audio = Mpeg1Frames128Kbps44Khz(100);
        ElevenLabsTtsProvider provider = new(
            new FakeFactory(audio),
            NullLogger<ElevenLabsTtsProvider>.Instance,
            "key"
        );

        TtsSynthesisResult result = await provider.SynthesizeAsync("hello", "voice");

        result.AudioData.Should().Equal(audio);
        result.DurationMs.Should().Be(2612);
    }
}
