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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Tests.Localization;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Music;

public sealed class MusicTemplateHelpersTests
{
    [Fact]
    public async Task The_scan_registers_one_music_provider_that_owns_the_music_keys()
    {
        await using MusicHelperFixture fixture = new();

        ITemplateHelperProvider? provider = fixture.Registry.ProviderFor("music.now.title");

        provider.Should().NotBeNull();
        provider!.Namespace.Should().Be("music");
        fixture.Registry.ProviderFor("music.queue.list").Should().BeSameAs(provider);
        fixture.Registry.ProviderFor("music.nope").Should().BeNull();
    }

    [Fact]
    public async Task Every_entry_has_an_english_string_a_dutch_string_and_a_sample()
    {
        await using MusicHelperFixture fixture = new();
        DashboardStringsXmlCatalog strings = new();
        List<TemplateHelperEntry> entries = MusicEntries(fixture);

        entries.Should().HaveCountGreaterThan(40);
        foreach (TemplateHelperEntry entry in entries)
        {
            strings
                .TryGetEnglish(entry.Description.Key, out string english)
                .Should()
                .BeTrue(entry.Key);
            strings.TryGetDutch(entry.Description.Key, out string dutch).Should().BeTrue(entry.Key);
            english.Should().NotBeNullOrWhiteSpace(entry.Key);
            dutch.Should().NotBe(english, entry.Key);
            entry.Sample.Should().NotBeNullOrEmpty(entry.Key);
        }
    }

    [Fact]
    public async Task Every_sample_is_plain_text_that_renders_to_itself_through_the_resolver()
    {
        await using MusicHelperFixture fixture = new();

        foreach (TemplateHelperEntry entry in MusicEntries(fixture))
        {
            string rendered = await fixture.RenderAsync(entry.Sample);

            rendered.Should().Be(entry.Sample, entry.Key);
        }
    }

    [Fact]
    public async Task Ten_music_tokens_in_one_render_read_each_service_once()
    {
        await using MusicHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{music.now.title} {music.now.artist} {music.now.progress} {music.now.percent} {music.now.bar:5} "
                + "{music.queue.length} {music.queue.next} {music.queue.wait} {music.queue.mine.count} {music.history.last}"
        );

        rendered
            .Should()
            .Be("Song A Artist A 1:23 40% ▬▬🔘▬▬ 5 Song B by Artist B 7m 40s 2 Song Z by Artist Z");
        await fixture
            .Music.Received(1)
            .GetNowPlayingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<bool>());
        await fixture
            .Music.Received(1)
            .GetQueueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        fixture.Music.Received(1).SnapshotQueue(Arg.Any<string>());
        await fixture
            .History.Received(1)
            .GetRecentAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_render_without_music_tokens_calls_no_music_service()
    {
        await using MusicHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("hello {text.upper:world}");

        rendered.Should().Be("hello WORLD");
        await fixture
            .Music.DidNotReceive()
            .GetNowPlayingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task When_nothing_plays_a_state_reads_false_and_a_value_prints_the_token_as_written()
    {
        await using MusicHelperFixture fixture = new(idle: true);

        string rendered = await fixture.RenderAsync(
            "{music.now.isPlaying} {music.queue.isEmpty} {music.queue.length} {music.now.status} {music.now.title}"
        );

        rendered.Should().Be("false true 0 idle {music.now.title}");
    }

    [Fact]
    public async Task A_viewer_who_is_not_in_the_queue_has_no_place_and_no_songs()
    {
        await using MusicHelperFixture fixture = new();
        Dictionary<string, string> seeds = MusicHelperFixture.Seeds();
        seeds["user"] = "Stranger";

        string rendered = await fixture.RenderAsync(
            "{music.queue.mine.count} [{music.queue.position}] [{music.queue.wait}]",
            seeds
        );

        rendered.Should().Be("0 [{music.queue.position}] [{music.queue.wait}]");
    }

    [Fact]
    public async Task An_argument_outside_the_queue_or_a_bad_width_prints_the_token_as_written()
    {
        await using MusicHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{music.queue.item:9} {music.queue.item:x} {music.now.bar:0} {music.history.list:0}"
        );

        rendered
            .Should()
            .Be(
                "{music.queue.item:9} {music.queue.item:x} {music.now.bar:0} {music.history.list:0}"
            );
    }

    private static List<TemplateHelperEntry> MusicEntries(MusicHelperFixture fixture) =>
        [
            .. fixture.Registry.All.Where(e =>
                fixture.Registry.ProviderFor(e.Key) is { Namespace: "music" }
            ),
        ];
}
