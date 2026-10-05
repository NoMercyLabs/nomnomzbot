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
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Domain.Tts.Interfaces;
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves one TTS request that carries several segments becomes ONE audio: Azure and Edge build a single
/// <c>&lt;speak&gt;</c> with one <c>&lt;voice&gt;</c> per segment and a <c>&lt;break&gt;</c> between them, and
/// ElevenLabs (no SSML) synthesizes every segment and joins the mp3 bytes into one clip.
/// </summary>
public sealed class TtsSegmentSynthesisTests
{
    private static readonly IReadOnlyList<TtsSegment> TwoVoices =
    [
        new(
            "Hey <chat> & friends",
            "en-US-GuyNeural",
            RatePercent: -20,
            PitchPercent: 10,
            BreakAfterMs: 600
        ),
        new("Second line", "en-US-AriaNeural"),
    ];

    private static void AssertOneSpeakTwoVoicesAndABreakBetween(string ssml)
    {
        XDocument doc = XDocument.Parse(ssml);

        doc.Descendants().Count(e => e.Name.LocalName == "speak").Should().Be(1);
        List<XElement> children = doc.Root!.Elements().ToList();
        children.Select(e => e.Name.LocalName).Should().Equal("voice", "break", "voice");

        children[0].Attribute("name")!.Value.Should().Be("en-US-GuyNeural");
        children[0]
            .Descendants()
            .Single(e => e.Name.LocalName == "prosody")
            .Value.Should()
            .Be("Hey <chat> & friends");
        children[1].Attribute("time")!.Value.Should().Be("600ms");
        children[2].Attribute("name")!.Value.Should().Be("en-US-AriaNeural");
        children[2].Value.Trim().Should().Be("Second line");

        ssml.Should().Contain("rate='-20%'").And.Contain("pitch='+10%'");
        ssml.Should().Contain("Hey &lt;chat&gt; &amp; friends").And.NotContain("<chat>");
    }

    [Fact]
    public void Edge_BuildSsml_Segments_EmitsOneSpeakWithTwoVoicesAndABreakBetween() =>
        AssertOneSpeakTwoVoicesAndABreakBetween(EdgeTtsProvider.BuildSsml(TwoVoices));

    [Fact]
    public void Azure_BuildSsml_Segments_EmitsOneSpeakWithTwoVoicesAndABreakBetween() =>
        AssertOneSpeakTwoVoicesAndABreakBetween(AzureTtsProvider.BuildSsml(TwoVoices));

    [Fact]
    public void BuildSsml_Segments_ZeroGapAddsNoBreak_AndNoBreakFollowsTheLastSegment()
    {
        IReadOnlyList<TtsSegment> segments =
        [
            new("one", "v1", BreakAfterMs: 0),
            new("two", "v2", BreakAfterMs: 500),
        ];

        foreach (
            string ssml in new[]
            {
                EdgeTtsProvider.BuildSsml(segments),
                AzureTtsProvider.BuildSsml(segments),
            }
        )
            XDocument
                .Parse(ssml)
                .Descendants()
                .Count(e => e.Name.LocalName == "break")
                .Should()
                .Be(0);
    }

    [Fact]
    public void BuildSsml_Segments_ClampsAnAbsurdBreak_AndEscapesAVoiceBreakout()
    {
        IReadOnlyList<TtsSegment> segments =
        [
            new("one", "v1'/><speak>x</speak>", BreakAfterMs: 99_999_999),
            new("two", "v2"),
        ];

        foreach (
            string ssml in new[]
            {
                EdgeTtsProvider.BuildSsml(segments),
                AzureTtsProvider.BuildSsml(segments),
            }
        )
        {
            XDocument doc = XDocument.Parse(ssml);
            doc.Descendants().Count(e => e.Name.LocalName == "speak").Should().Be(1);
            doc.Descendants()
                .Single(e => e.Name.LocalName == "break")
                .Attribute("time")!
                .Value.Should()
                .Be($"{TtsProsody.MaxBreakMs}ms");
        }
    }

    // MPEG-1 Layer III, 128 kbit/s, 44.1 kHz: 417-byte frames of 1152 samples.
    private static byte[] Frames(int count, byte marker)
    {
        byte[] all = new byte[count * 417];
        for (int i = 0; i < count; i++)
        {
            byte[] header = [0xFF, 0xFB, 0x90, 0xC0];
            header.CopyTo(all, i * 417);
            all[(i * 417) + 4] = marker;
        }
        return all;
    }

    private sealed class RecordingHandler(Dictionary<string, byte[]> audioByVoice)
        : HttpMessageHandler
    {
        public List<(string Voice, string Body)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string voice = request.RequestUri!.Segments[^1];
            Calls.Add((voice, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(audioByVoice[voice]) };
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    [Fact]
    public async Task ElevenLabs_SynthesizeSegmentsAsync_MakesOneSynthCallPerSegment_AndReturnsOneConcatenatedAudio()
    {
        byte[] first = Frames(10, 0xA1);
        byte[] second = Frames(20, 0xB2);
        RecordingHandler handler = new(new() { ["voice-a"] = first, ["voice-b"] = second });
        ElevenLabsTtsProvider provider = new(
            new FakeFactory(handler),
            NullLogger<ElevenLabsTtsProvider>.Instance,
            "key"
        );

        TtsSynthesisResult result = await provider.SynthesizeSegmentsAsync([
            new("hello there", "voice-a", BreakAfterMs: 400),
            new("and you", "voice-b"),
        ]);

        handler.Calls.Select(c => c.Voice).Should().Equal("voice-a", "voice-b");
        handler.Calls[0].Body.Should().Contain("hello there");
        handler.Calls[1].Body.Should().Contain("and you");
        result.AudioData.Should().Equal([.. first, .. second]);
        // 30 frames * 1152 samples / 44100 Hz = 783.67 ms.
        result.DurationMs.Should().Be(784);
        result.Provider.Should().Be("elevenlabs");
        result.VoiceId.Should().Be("voice-a");
    }

    [Fact]
    public async Task ElevenLabs_SynthesizeSegmentsAsync_OneSegmentFailing_ReturnsNoAudioRatherThanAHalfClip()
    {
        RecordingHandler handler = new(new() { ["voice-a"] = Frames(10, 0xA1), ["voice-b"] = [] });
        ElevenLabsTtsProvider provider = new(
            new FakeFactory(handler),
            NullLogger<ElevenLabsTtsProvider>.Instance,
            "key"
        );

        TtsSynthesisResult result = await provider.SynthesizeSegmentsAsync([
            new("hello", "voice-a"),
            new("again", "voice-b"),
        ]);

        result.AudioData.Should().BeEmpty();
        result.DurationMs.Should().Be(0);
    }
}
