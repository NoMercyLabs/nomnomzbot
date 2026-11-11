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

using System.Net;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Domain.Tts.Interfaces;
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves one TTS request that carries several segments becomes ONE audio, each provider by its own rules.
/// Azure builds a single <c>&lt;speak&gt;</c> with one <c>&lt;voice&gt;</c> per segment and the pause inside
/// that voice. Edge takes no pause tag at all, so it cuts the line at each pause and joins the clips with
/// silent frames. ElevenLabs (no SSML) synthesizes every segment and joins the mp3 bytes into one clip.
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

    // Azure refuses a <break> that sits between two <voice> elements (BadRequest, seen live 2026-11-11):
    // the pause belongs inside the voice that speaks before it.
    [Fact]
    public void Azure_BuildSsml_Segments_EmitsOneSpeakWithTwoVoices_AndThePauseInsideTheFirstVoice()
    {
        string ssml = AzureTtsProvider.BuildSsml(TwoVoices);
        XDocument doc = XDocument.Parse(ssml);

        doc.Descendants().Count(e => e.Name.LocalName == "speak").Should().Be(1);
        List<XElement> voices = doc.Root!.Elements().ToList();
        voices.Select(e => e.Name.LocalName).Should().Equal("voice", "voice");

        voices[0].Attribute("name")!.Value.Should().Be("en-US-GuyNeural");
        voices[0].Elements().Select(e => e.Name.LocalName).Should().Equal("prosody", "break");
        voices[0].Elements().First().Value.Should().Be("Hey <chat> & friends");
        voices[0].Elements().Last().Attribute("time")!.Value.Should().Be("600ms");
        voices[1].Attribute("name")!.Value.Should().Be("en-US-AriaNeural");
        voices[1].Elements().Select(e => e.Name.LocalName).Should().Equal("prosody");
        voices[1].Value.Should().Be("Second line");

        ssml.Should().Contain("rate='-20%'").And.Contain("pitch='+10%'");
        ssml.Should().Contain("Hey &lt;chat&gt; &amp; friends").And.NotContain("<chat>");
    }

    // The read-aloud endpoint answers any request that carries a <break> with no audio at all (probed
    // 2026-11-11), so a pause ends the request and the silence is added between the clips.
    [Fact]
    public void Edge_ChunkRuns_EndsARequestAfterAPause()
    {
        List<List<TtsSegment>> runs = EdgeTtsProvider.ChunkRuns(
            EdgeTtsProvider.VoiceRuns([
                new("a", "bot", BreakAfterMs: 320),
                new("b", "bot"),
                new("c", "bot"),
            ])
        );

        runs.Select(r => string.Concat(r.Select(s => s.Text))).Should().Equal("a", "b c");
    }

    [Fact]
    public void Edge_BuildRunSsml_NeverCarriesABreak_EvenWhenASegmentAsksForAPause()
    {
        string ssml = EdgeTtsProvider.BuildRunSsml([
            new("one", "en-US-AnaNeural", BreakAfterMs: 300),
            new("two", "en-US-AnaNeural", BreakAfterMs: 300),
        ]);

        XDocument.Parse(ssml).Descendants().Should().NotContain(e => e.Name.LocalName == "break");
    }

    [Fact]
    public void Edge_Join_PutsThePauseAsSilentFramesBetweenTheClips_AndNoneAfterTheLast()
    {
        byte[] first = [1, 2, 3];
        byte[] second = [4, 5];
        List<List<TtsSegment>> runs =
        [
            [new("a", "bot", BreakAfterMs: 600)],
            [new("b", "bot", BreakAfterMs: 900)],
        ];

        byte[] joined = EdgeTtsProvider.JoinClips(runs, [first, second]);

        byte[] silence = Mp3Silence.OfMilliseconds(600);
        joined.Should().Equal([.. first, .. silence, .. second]);
        Mp3Duration.ToMilliseconds(silence, 48).Should().Be(600);
    }

    [Fact]
    public void Edge_Join_ClampsAnAbsurdPause()
    {
        List<List<TtsSegment>> runs =
        [
            [new("a", "bot", BreakAfterMs: 99_999_999)],
            [new("b", "bot")],
        ];

        byte[] joined = EdgeTtsProvider.JoinClips(
            runs,
            [
                [],
                [],
            ]
        );

        Mp3Duration
            .ToMilliseconds(joined, 48)
            .Should()
            .BeInRange(TtsProsody.MaxBreakMs, TtsProsody.MaxBreakMs + 24);
    }

    [Fact]
    public void Mp3Silence_ZeroOrLess_IsEmpty()
    {
        Mp3Silence.OfMilliseconds(0).Should().BeEmpty();
        Mp3Silence.OfMilliseconds(-5).Should().BeEmpty();
    }

    [Fact]
    public void Edge_VoiceRuns_KeepsConsecutiveSegmentsOfOneVoiceTogether_AndSplitsWhereTheVoiceChanges()
    {
        List<List<TtsSegment>> runs = EdgeTtsProvider.VoiceRuns([
            new("a", "bot"),
            new("b", "bot"),
            new("c", "viewer"),
            new("d", "bot"),
        ]);

        // Two blocks that sound the same are one stretch of speech.
        runs.Select(r => string.Concat(r.Select(s => s.Text))).Should().Equal("a b", "c", "d");
        runs.Select(r => r[0].VoiceId).Should().Equal("bot", "viewer", "bot");
    }

    // Edge returns no audio for a one-voice request carrying 3 or more segments (probed 2026-11-05:
    // 1-2 segments synthesize fine, 3 reliably returns nothing), so a run longer than the proven-safe
    // size is cut into same-voice chunks the caller sends as separate requests.
    [Fact]
    public void Edge_ChunkRuns_SplitsARunLongerThanTheSafeSize_AndLeavesShorterRunsAlone()
    {
        List<List<TtsSegment>> runs = EdgeTtsProvider.ChunkRuns(
            EdgeTtsProvider.VoiceRuns([
                new("a", "bot", RatePercent: 1),
                new("b", "bot", RatePercent: 2),
                new("c", "bot", RatePercent: 3),
                new("d", "bot", RatePercent: 4),
                new("e", "bot", RatePercent: 5),
                new("f", "viewer"),
            ])
        );

        runs.Select(r => string.Concat(r.Select(s => s.Text))).Should().Equal("ab", "cd", "e", "f");
        runs.Should().OnlyContain(r => r.Count <= EdgeTtsProvider.MaxSegmentsPerRequest);
    }

    // Edge returns no audio for a document with two <voice> elements, even when both name the same voice.
    [Fact]
    public void Edge_BuildRunSsml_TwoSegmentsOfOneVoice_EmitsOneVoiceElementWithOneProsodyEach()
    {
        string ssml = EdgeTtsProvider.BuildRunSsml([
            new("Stoney_Eagle threw 100 bits our way. They also said:", "en-US-AnaNeural"),
            new("Great <stream> & more!", "en-US-AnaNeural", RatePercent: 10, BreakAfterMs: 300),
        ]);

        XDocument doc = XDocument.Parse(ssml);
        doc.Descendants().Count(e => e.Name.LocalName == "voice").Should().Be(1);
        XElement voice = doc.Root!.Elements().Single();
        voice.Attribute("name")!.Value.Should().Be("en-US-AnaNeural");
        voice.Elements().Select(e => e.Name.LocalName).Should().Equal("prosody", "prosody");
        voice
            .Elements()
            .Select(e => e.Value)
            .Should()
            .Equal(
                "Stoney_Eagle threw 100 bits our way. They also said:",
                "Great <stream> & more!"
            );
    }

    [Fact]
    public void Azure_BuildSsml_Segments_ZeroGapAddsNoBreak_AndNoBreakFollowsTheLastSegment()
    {
        string ssml = AzureTtsProvider.BuildSsml([
            new("one", "v1", BreakAfterMs: 0),
            new("two", "v2", BreakAfterMs: 500),
        ]);

        XDocument.Parse(ssml).Descendants().Should().NotContain(e => e.Name.LocalName == "break");
    }

    [Fact]
    public void Azure_BuildSsml_Segments_ClampsAnAbsurdBreak_AndEscapesAVoiceBreakout()
    {
        string ssml = AzureTtsProvider.BuildSsml([
            new("one", "v1'/><speak>x</speak>", BreakAfterMs: 99_999_999),
            new("two", "v2"),
        ]);

        XDocument doc = XDocument.Parse(ssml);
        doc.Descendants().Count(e => e.Name.LocalName == "speak").Should().Be(1);
        doc.Descendants()
            .Single(e => e.Name.LocalName == "break")
            .Attribute("time")!
            .Value.Should()
            .Be($"{TtsProsody.MaxBreakMs}ms");
    }

    // The same three blocks, one voice: plain words, a stressed word, plain words. Every provider gets this list.
    private static readonly IReadOnlyList<TtsSegment> StressedSentence =
    [
        new("Duka, that is", "en-US-AnaNeural", RatePercent: 4, VolumePercent: -10),
        new("not a fix", "en-US-AnaNeural", RatePercent: 4, VolumePercent: -10, Stressed: true),
        new("<at all>", "en-US-AnaNeural", RatePercent: 4, VolumePercent: -10),
    ];

    // Azure speaks blocks of one voice as one flowing sentence: one <voice>, one prosody per block. It has no
    // stress tag for most voices (the emphasis element works for three voices only), so a stressed block is
    // slower, higher and louder than the block asked for.
    [Fact]
    public void Azure_BuildSsml_BlocksOfOneVoice_ShareOneVoice_AndAStressedBlockIsSlowerHigherAndLouder()
    {
        XDocument doc = XDocument.Parse(AzureTtsProvider.BuildSsml(StressedSentence));

        XElement voice = doc.Root!.Elements().Single();
        voice.Value.Should().Be("Duka, that is not a fix <at all>");
        List<XElement> blocks = voice.Elements().ToList();
        blocks.Select(e => e.Name.LocalName).Should().Equal("prosody", "prosody", "prosody");
        blocks.Select(e => e.Attribute("rate")!.Value).Should().Equal("+4%", "-11%", "+4%");
        blocks.Select(e => e.Attribute("pitch")!.Value).Should().Equal("+0%", "+12%", "+0%");
        blocks.Select(e => e.Attribute("volume")!.Value).Should().Equal("-10%", "+20%", "-10%");
    }

    [Fact]
    public void Azure_BuildSsml_APlainBlockWithNoVolume_CarriesNoVolume_AndAVoiceChangeOpensANewVoice()
    {
        XDocument doc = XDocument.Parse(
            AzureTtsProvider.BuildSsml([
                new("one", "v1"),
                new("two", "v1", BreakAfterMs: 250),
                new("three", "v2"),
            ])
        );

        List<XElement> voices = doc.Root!.Elements().ToList();
        voices.Select(v => v.Attribute("name")!.Value).Should().Equal("v1", "v2");
        voices[0]
            .Elements()
            .Select(e => e.Name.LocalName)
            .Should()
            .Equal("prosody", "prosody", "break");
        voices[0].Elements().First().Attribute("volume").Should().BeNull();
    }

    // Edge answers a request with three or more prosody elements, or a nested one, with no audio at all
    // (probed 2026-11-11). It cannot stress, so blocks that sound the same become one stretch of plain speech:
    // the stressed sentence is ONE prosody in ONE request, with its volume.
    [Fact]
    public void Edge_SpeaksTheStressedSentenceAsOnePlainStretch_WithItsVolume()
    {
        List<List<TtsSegment>> requests = EdgeTtsProvider.ChunkRuns(
            EdgeTtsProvider.VoiceRuns(StressedSentence)
        );

        requests.Should().ContainSingle();
        XElement voice = XDocument
            .Parse(EdgeTtsProvider.BuildRunSsml(requests[0]))
            .Root!.Elements()
            .Single();
        XElement block = voice.Elements().Single();
        block.Value.Should().Be("Duka, that is not a fix <at all>");
        block.Attribute("volume")!.Value.Should().Be("-10%");
        block.Elements().Should().BeEmpty();
    }

    [Fact]
    public void Edge_VoiceRuns_KeepBlocksThatSoundDifferentApart_AndNeverJoinAcrossAPause()
    {
        List<List<TtsSegment>> runs = EdgeTtsProvider.VoiceRuns([
            new("a", "bot", BreakAfterMs: 300),
            new("b", "bot"),
            new("c", "bot", PitchPercent: 8),
            new("d", "bot", PitchPercent: 8, VolumePercent: 20),
        ]);

        runs.Single().Select(block => block.Text).Should().Equal("a", "b", "c", "d");
    }

    // ElevenLabs has no SSML at all: a run of one voice is one text in one call.
    [Fact]
    public void ElevenLabs_VoiceRuns_JoinTheBlocksOfOneVoiceIntoOneText()
    {
        ElevenLabsTtsProvider
            .VoiceRuns([.. StressedSentence, new("and you", "voice-b")])
            .Should()
            .Equal(("en-US-AnaNeural", "Duka, that is not a fix <at all>"), ("voice-b", "and you"));
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
