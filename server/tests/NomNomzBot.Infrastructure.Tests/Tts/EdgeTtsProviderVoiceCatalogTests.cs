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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Tts.Interfaces;
using NomNomzBot.Infrastructure.Tts;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the Edge voice catalogue is the LIVE read-aloud list, not a curated subset (full external-API
/// coverage): the list payload maps ShortName→id with derived given-name display names (including obscure
/// locales like iu-Cans-CA and multilingual variants legacy users actually picked); any fetch/parse failure
/// degrades to the curated fallback so the upsert-only catalogue sync never loses the well-known voices; and
/// the sync lands the fetched set beside other providers' rows without wiping them.
/// </summary>
public sealed class EdgeTtsProviderVoiceCatalogTests
{
    // Real-shaped read-aloud list payload: a plain US voice, an Inuktitut voice with a script subtag in the
    // locale, a multilingual variant, and a Russian voice — the spread legacy imports actually reference.
    private const string SamplePayload = """
        [
          {
            "Name": "Microsoft Server Speech Text to Speech Voice (en-US, AnaNeural)",
            "ShortName": "en-US-AnaNeural",
            "Gender": "Female",
            "Locale": "en-US",
            "SuggestedCodec": "audio-24khz-48kbitrate-mono-mp3",
            "FriendlyName": "Microsoft Ana Online (Natural) - English (United States)",
            "Status": "GA"
          },
          {
            "Name": "Microsoft Server Speech Text to Speech Voice (iu-Cans-CA, TaqqiqNeural)",
            "ShortName": "iu-Cans-CA-TaqqiqNeural",
            "Gender": "Male",
            "Locale": "iu-Cans-CA",
            "SuggestedCodec": "audio-24khz-48kbitrate-mono-mp3",
            "FriendlyName": "Microsoft Taqqiq Online (Natural) - Inuktitut (Syllabics, Canada)",
            "Status": "GA"
          },
          {
            "Name": "Microsoft Server Speech Text to Speech Voice (en-US, EmmaMultilingualNeural)",
            "ShortName": "en-US-EmmaMultilingualNeural",
            "Gender": "Female",
            "Locale": "en-US",
            "SuggestedCodec": "audio-24khz-48kbitrate-mono-mp3",
            "FriendlyName": "Microsoft EmmaMultilingual Online (Natural) - English (United States)",
            "Status": "GA"
          },
          {
            "Name": "Microsoft Server Speech Text to Speech Voice (ru-RU, SvetlanaNeural)",
            "ShortName": "ru-RU-SvetlanaNeural",
            "Gender": "Female",
            "Locale": "ru-RU",
            "SuggestedCodec": "audio-24khz-48kbitrate-mono-mp3",
            "FriendlyName": "Microsoft Svetlana Online (Natural) - Russian (Russia)",
            "Status": "GA"
          }
        ]
        """;

    // A handler-backed factory so GetVoicesAsync exercises its real HTTP + fallback path deterministically.
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            _respond = respond;

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(_respond(request));
        }
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static EdgeTtsProvider Build(StubHandler handler) =>
        new(
            TimeProvider.System,
            new StubHttpClientFactory(handler),
            NullLogger<EdgeTtsProvider>.Instance
        );

    [Fact]
    public void ParseVoiceList_MapsShortNameIds_LocaleGender_AndDerivedNames()
    {
        IReadOnlyList<TtsVoiceInfo> voices = EdgeTtsProvider.ParseVoiceList(SamplePayload);

        voices.Should().HaveCount(4);
        voices.Should().OnlyContain(v => v.Provider == "edge");

        TtsVoiceInfo ana = voices.Single(v => v.Id == "en-US-AnaNeural");
        ana.Name.Should().Be("Ana");
        ana.DisplayName.Should().Be("Ana (US)");
        ana.Locale.Should().Be("en-US");
        ana.Gender.Should().Be("Female");

        // The obscure-locale voice a legacy user actually assigned: script subtag in the locale, region
        // still resolved for the display tag.
        TtsVoiceInfo taqqiq = voices.Single(v => v.Id == "iu-Cans-CA-TaqqiqNeural");
        taqqiq.Name.Should().Be("Taqqiq");
        taqqiq.DisplayName.Should().Be("Taqqiq (CA)");
        taqqiq.Locale.Should().Be("iu-Cans-CA");
        taqqiq.Gender.Should().Be("Male");

        // Multilingual variants keep their variant word, spaced out of the camel-cased short name.
        TtsVoiceInfo emma = voices.Single(v => v.Id == "en-US-EmmaMultilingualNeural");
        emma.Name.Should().Be("Emma Multilingual");
        emma.DisplayName.Should().Be("Emma Multilingual (US)");

        TtsVoiceInfo svetlana = voices.Single(v => v.Id == "ru-RU-SvetlanaNeural");
        svetlana.Name.Should().Be("Svetlana");
        svetlana.DisplayName.Should().Be("Svetlana (RU)");
    }

    [Fact]
    public async Task GetVoicesAsync_ReturnsTheLiveList_FetchedFromTheReadAloudEndpoint()
    {
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SamplePayload),
        });

        IReadOnlyList<TtsVoiceInfo> voices = await Build(handler).GetVoicesAsync();

        voices
            .Select(v => v.Id)
            .Should()
            .BeEquivalentTo([
                "en-US-AnaNeural",
                "iu-Cans-CA-TaqqiqNeural",
                "en-US-EmmaMultilingualNeural",
                "ru-RU-SvetlanaNeural",
            ]);
        handler
            .LastRequestUri!.AbsoluteUri.Should()
            .Be(
                "https://speech.platform.bing.com/consumer/speech/synthesize/readaloud/voices/list?trustedclienttoken=6A5AA1D4EAFF4E9FB37E23D68491D6F4"
            );
    }

    [Fact]
    public async Task GetVoicesAsync_HttpFailure_ReturnsTheCuratedFallback()
    {
        StubHandler handler = new(_ => throw new HttpRequestException("connection refused"));

        IReadOnlyList<TtsVoiceInfo> voices = await Build(handler).GetVoicesAsync();

        // The curated subset, verbatim — specific well-known entries, all Edge, never empty.
        voices.Should().NotBeEmpty();
        voices.Should().OnlyContain(v => v.Provider == "edge");
        TtsVoiceInfo aria = voices.Single(v => v.Id == "en-US-AriaNeural");
        aria.Name.Should().Be("Aria");
        aria.DisplayName.Should().Be("Aria (US)");
        TtsVoiceInfo ryan = voices.Single(v => v.Id == "en-GB-RyanNeural");
        ryan.Gender.Should().Be("Male");
        voices.Select(v => v.Id).Should().Contain("ja-JP-NanamiNeural");
    }

    [Fact]
    public async Task GetVoicesAsync_MalformedPayload_ReturnsTheCuratedFallback()
    {
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>maintenance</html>"),
        });

        IReadOnlyList<TtsVoiceInfo> voices = await Build(handler).GetVoicesAsync();

        voices.Should().BeSameAs(EdgeTtsProvider.FallbackVoices);
    }

    [Fact]
    public async Task GetVoicesAsync_ErrorStatus_ReturnsTheCuratedFallback()
    {
        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        IReadOnlyList<TtsVoiceInfo> voices = await Build(handler).GetVoicesAsync();

        voices.Should().BeSameAs(EdgeTtsProvider.FallbackVoices);
    }

    [Fact]
    public async Task CatalogSync_UpsertsTheFetchedSet_WithoutWipingOtherProviders()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        // Another provider's voice already in the catalogue, plus a seeded Edge default the fetch refreshes.
        db.TtsVoices.Add(
            new TtsVoice
            {
                Id = "el-rachel",
                Name = "Rachel",
                DisplayName = "Rachel",
                Locale = "en-US",
                Gender = "female",
                Provider = "elevenlabs",
            }
        );
        db.TtsVoices.Add(
            new TtsVoice
            {
                Id = "en-US-AnaNeural",
                Name = "Ana",
                DisplayName = "Ana (US)",
                Locale = "en-US",
                Gender = "Female",
                Provider = "edge",
                IsDefault = true,
            }
        );
        await db.SaveChangesAsync();

        StubHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(SamplePayload),
        });
        TtsVoiceCatalogSync sync = new(
            [Build(handler)],
            db,
            NullLogger<TtsVoiceCatalogSync>.Instance
        );

        await sync.SyncAsync();

        List<TtsVoice> rows = await db.TtsVoices.ToListAsync();
        rows.Select(v => v.Id)
            .Should()
            .BeEquivalentTo([
                "el-rachel", // other provider untouched
                "en-US-AnaNeural", // refreshed, not duplicated
                "iu-Cans-CA-TaqqiqNeural",
                "en-US-EmmaMultilingualNeural",
                "ru-RU-SvetlanaNeural",
            ]);
        rows.Single(v => v.Id == "el-rachel").Provider.Should().Be("elevenlabs");
        TtsVoice ana = rows.Single(v => v.Id == "en-US-AnaNeural");
        ana.IsDefault.Should().BeTrue("a metadata refresh never clears the seeded default");
        rows.Single(v => v.Id == "iu-Cans-CA-TaqqiqNeural").DisplayName.Should().Be("Taqqiq (CA)");
    }
}
