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
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Tts.Interfaces;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the searchable voice catalogue (tts.md §3.6, decision 5): free-text `Q` matches name/tags, the
/// scalar filters (locale/gender/provider/accent) narrow, results page, and the rich metadata (styles/tags
/// parsed from their JSON columns, accent, preview url) round-trips onto the DTO. When the catalogue table is
/// empty the search falls back to the live provider list.
/// </summary>
public sealed class TtsConfigServiceSearchVoicesTests
{
    private static TtsConfigService Build(TtsTestDbContext db, ITtsService? ttsService = null) =>
        new(
            db,
            ttsService ?? Substitute.For<ITtsService>(),
            Substitute.For<IEventBus>(),
            Substitute.For<ISubjectKeyService>()
        );

    private static async Task<TtsTestDbContext> SeededCatalogueAsync()
    {
        TtsTestDbContext db = TtsTestDbContext.New();
        db.TtsVoices.AddRange(
            new TtsVoice
            {
                Id = "en-US-AriaNeural",
                Name = "AriaNeural",
                DisplayName = "Aria (US)",
                Locale = "en-US",
                Gender = "Female",
                Provider = "edge",
                Accent = "American",
                TagsJson = "[\"narration\",\"news\"]",
                StylesJson = "[\"cheerful\"]",
            },
            new TtsVoice
            {
                Id = "en-US-GuyNeural",
                Name = "GuyNeural",
                DisplayName = "Guy (US)",
                Locale = "en-US",
                Gender = "Male",
                Provider = "edge",
                Accent = "American",
            },
            new TtsVoice
            {
                Id = "en-GB-SoniaNeural",
                Name = "SoniaNeural",
                DisplayName = "Sonia (GB)",
                Locale = "en-GB",
                Gender = "Female",
                Provider = "edge",
                Accent = "British",
                TagsJson = "[\"gaming\"]",
            },
            new TtsVoice
            {
                Id = "ja-JP-NanamiNeural",
                Name = "NanamiNeural",
                DisplayName = "Nanami (JP)",
                Locale = "ja-JP",
                Gender = "Female",
                Provider = "edge",
                Accent = "Japanese",
            },
            new TtsVoice
            {
                Id = "el-Rachel",
                Name = "Rachel",
                DisplayName = "Rachel",
                Locale = "en-US",
                Gender = "Female",
                Provider = "elevenlabs",
                Accent = "American",
                Description = "Warm conversational narrator",
                PreviewUrl = "https://cdn.elevenlabs/rachel.mp3",
                TagsJson = "[\"narration\",\"audiobook\"]",
                StylesJson = "[\"calm\"]",
            }
        );
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Free_text_query_matches_a_tag()
    {
        TtsTestDbContext db = await SeededCatalogueAsync();
        TtsConfigService sut = Build(db);

        Result<PagedList<TtsVoiceDto>> result = await sut.SearchVoicesAsync(
            new TtsVoiceQuery(Q: "gaming")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().ContainSingle().Which.Id.Should().Be("en-GB-SoniaNeural");
    }

    [Fact]
    public async Task Free_text_query_matches_a_name_case_insensitively()
    {
        TtsTestDbContext db = await SeededCatalogueAsync();
        TtsConfigService sut = Build(db);

        Result<PagedList<TtsVoiceDto>> result = await sut.SearchVoicesAsync(
            new TtsVoiceQuery(Q: "aria")
        );

        result
            .Value.Items.Select(v => v.Id)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("en-US-AriaNeural");
    }

    [Fact]
    public async Task Locale_and_gender_filters_narrow_the_result()
    {
        TtsTestDbContext db = await SeededCatalogueAsync();
        TtsConfigService sut = Build(db);

        Result<PagedList<TtsVoiceDto>> result = await sut.SearchVoicesAsync(
            new TtsVoiceQuery(Locale: "en-US", Gender: "Male")
        );

        result.Value.Items.Should().ContainSingle().Which.Id.Should().Be("en-US-GuyNeural");
    }

    [Fact]
    public async Task Accent_filter_narrows_the_result()
    {
        TtsTestDbContext db = await SeededCatalogueAsync();
        TtsConfigService sut = Build(db);

        Result<PagedList<TtsVoiceDto>> result = await sut.SearchVoicesAsync(
            new TtsVoiceQuery(Accent: "british")
        );

        result.Value.Items.Should().ContainSingle().Which.Id.Should().Be("en-GB-SoniaNeural");
    }

    [Fact]
    public async Task Provider_filter_narrows_to_elevenlabs_and_carries_rich_metadata()
    {
        TtsTestDbContext db = await SeededCatalogueAsync();
        TtsConfigService sut = Build(db);

        Result<PagedList<TtsVoiceDto>> result = await sut.SearchVoicesAsync(
            new TtsVoiceQuery(Provider: "elevenlabs")
        );

        TtsVoiceDto voice = result.Value.Items.Should().ContainSingle().Subject;
        voice.Id.Should().Be("el-Rachel");
        voice.PreviewUrl.Should().Be("https://cdn.elevenlabs/rachel.mp3");
        voice.Description.Should().Be("Warm conversational narrator");
        voice.Tags.Should().Equal("narration", "audiobook");
        voice.Styles.Should().Equal("calm");
    }

    [Fact]
    public async Task Empty_query_pages_the_whole_catalogue_with_a_correct_total()
    {
        TtsTestDbContext db = await SeededCatalogueAsync();
        TtsConfigService sut = Build(db);

        Result<PagedList<TtsVoiceDto>> firstPage = await sut.SearchVoicesAsync(
            new TtsVoiceQuery(Page: 1, PageSize: 2)
        );

        firstPage.Value.TotalCount.Should().Be(5);
        firstPage.Value.Items.Should().HaveCount(2);
        firstPage.Value.HasNextPage.Should().BeTrue();
    }

    [Fact]
    public async Task Empty_catalogue_falls_back_to_the_live_provider_list()
    {
        TtsTestDbContext db = TtsTestDbContext.New(); // no rows seeded
        ITtsService ttsService = Substitute.For<ITtsService>();
        ttsService
            .GetAvailableVoicesAsync(Arg.Any<CancellationToken>())
            .Returns(
                new List<TtsVoiceInfo>
                {
                    new()
                    {
                        Id = "live-1",
                        Name = "Live",
                        DisplayName = "Live One",
                        Locale = "en-US",
                        Gender = "Female",
                        Provider = "edge",
                    },
                }
            );
        TtsConfigService sut = Build(db, ttsService);

        Result<PagedList<TtsVoiceDto>> result = await sut.SearchVoicesAsync(new TtsVoiceQuery());

        result.Value.Items.Should().ContainSingle().Which.Id.Should().Be("live-1");
    }
}
