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
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Localization;
using NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.People;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Arguments;

/// <summary>
/// Catalog section 7.7, the <c>args.&lt;n&gt;.user.&lt;key&gt;</c> rows: the n-th word of a command is read as a
/// viewer and answers any viewer key. Every Example renders to exactly its Output cell against the section 5
/// fixture (<c>!cmd @ViewerTwo hello big world</c>, channel A), through the real parser, registry and resolver.
/// </summary>
public sealed class ArgumentUserHelpersTableTests
{
    private const string Fixture = "@ViewerTwo hello big world";

    public static TheoryData<string, string, string> Rows =>
        new()
        {
            { Fixture, "{args.1.user.display}", "ViewerTwo" },
            { Fixture, "{args.1.user.login}", "viewertwo" },
            { Fixture, "{args.1.user.mention}", "@ViewerTwo" },
            { Fixture, "{args.1.user.platform}", "twitch" },
            { Fixture, "{args.1.user.shoutoutLink}", "https://twitch.tv/viewertwo" },
            { Fixture, "{args.1.user.points}", "800" },
            { Fixture, "{args.1.user.points.raw}", "800" },
            { Fixture, "{args.1.user.points.rank}", "17" },
            { Fixture, "{args.1.user.points.rank.ordinal}", "17th" },
            { Fixture, "{args.1.user.points} {args.1.user.points.name}", "800 nomz" },
            { Fixture, "{args.1.user.game}", "Minecraft" },
            { Fixture, "{args.1.user.title}", "Building castles" },
            { Fixture, "{args.1.user.bio}", "I build castles" },
            { Fixture, "{args.1.user.color}", "#FF4500" },
            {
                Fixture,
                "{args.1.user.avatar}",
                "https://static-cdn.jtvnw.net/jtv_user_pictures/viewertwo.png"
            },
            { Fixture, "{args.1.user.createdAt}", "2019-05-02" },
            { Fixture, "{args.1.user.accountAge}", "7 years, 5 months" },
            { Fixture, "{args.1.user.stats.streak}", "2" },
            { Fixture, "{args.1.user.stats.lastSeen}", "2026-10-25" },
            { Fixture, "{args.1.user.stats.bits}", "0" },
            { Fixture, "{args.1.user.stats.watchTime.hours}", "1" },
            { Fixture, "{args.1.user.followedAt}", "2026-01-05" },
            { Fixture, "{args.1.user.followAge.days}", "294" },
            { Fixture, "{args.1.user.followAge}", "9 months and 24 days" },
            { Fixture, "{if:{args.1.user.isFollower}|follows|does not follow}", "follows" },
            { Fixture, "{if:{args.1.user.isSub}|sub|not a sub}", "not a sub" },
            { Fixture, "{if:{args.1.user.isLive}|live now|offline}", "offline" },
            { Fixture, "{args.1.user.data.food:none}", "none" },
            { Fixture, "{args.1.user.data.food}", "" },
            { Fixture, "{args.1.user.quotes.count}", "1" },
            // Another position and another person.
            { "hello @ViewerTwo", "{args.2.user.display}", "ViewerTwo" },
            { "@ViewerTwo @StreamerFour", "{args.2.user.display}", "StreamerFour" },
            { "@ViewerTwo @StreamerFour", "{args.1.user.display}", "ViewerTwo" },
            {
                "@ViewerTwo @StreamerFour",
                "{if:{args.2.user.isLive}|live now|offline} {args.2.user.viewers}",
                "live now 18"
            },
            { "ViewerTwo", "{args.1.user.display}", "ViewerTwo" },
            {
                Fixture,
                "hi {args.1.user.mention}, {args.1.user.points} points",
                "hi @ViewerTwo, 800 points"
            },
            // Nothing to read: no such word, an empty mention, no channel row for a stranger.
            { Fixture, "[{args.9.user.display}]", "[]" },
            { Fixture, "[{args.5.user.points}]", "[]" },
            { "", "[{args.1.user.display}]", "[]" },
            { "@", "[{args.1.user.display}]", "[]" },
            { "hello", "[{args.1.user.points}]", "[0]" },
            { "hello", "[{args.1.user.display}]", "[hello]" },
            // An unknown key stays as written, so the author sees the mistake.
            { Fixture, "{args.1.user.nonsense}", "{args.1.user.nonsense}" },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task The_example_renders_to_exactly_the_output_cell(
        string arguments,
        string template,
        string expected
    )
    {
        await using PeopleHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            template,
            PeopleHelperFixture.ChannelA,
            Seeds(arguments)
        );

        rendered.Should().Be(expected);
    }

    [Fact]
    public async Task The_same_person_read_as_a_user_and_as_an_argument_answers_the_same()
    {
        await using PeopleHelperFixture fixture = new();

        string viaTarget = await fixture.RenderAsync(
            "{target.points}/{target.color}/{target.stats.streak}",
            PeopleHelperFixture.ChannelA
        );
        string viaArgument = await fixture.RenderAsync(
            "{args.1.user.points}/{args.1.user.color}/{args.1.user.stats.streak}",
            PeopleHelperFixture.ChannelA,
            Seeds(Fixture)
        );

        viaArgument.Should().Be(viaTarget).And.Be("800/#FF4500/2");
    }

    [Fact]
    public async Task Another_channel_reads_its_own_numbers_for_the_same_argument()
    {
        await using PeopleHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{args.1.user.points.name}/{args.1.user.points.rank}",
            PeopleHelperFixture.ChannelB,
            Seeds(Fixture)
        );

        rendered.Should().Be("coins/1");
    }

    [Fact]
    public async Task Every_user_entry_has_an_english_string_a_dutch_string_and_a_sample_that_renders_to_itself()
    {
        await using PeopleHelperFixture fixture = new();
        DashboardStringsXmlCatalog strings = new();
        List<TemplateHelperEntry> entries =
        [
            .. fixture
                .Registry.ProviderFor("args.1.user.display")!
                .Entries.Where(e => e.Key.Contains(".user.", StringComparison.Ordinal)),
        ];

        entries.Should().HaveCountGreaterThan(35);
        entries.Select(e => e.Key).Should().OnlyHaveUniqueItems();
        foreach (TemplateHelperEntry entry in entries)
        {
            strings
                .TryGetEnglish(entry.Description.Key, out string english)
                .Should()
                .BeTrue(entry.Key);
            strings.TryGetDutch(entry.Description.Key, out string dutch).Should().BeTrue(entry.Key);
            english.Should().NotBeNullOrWhiteSpace(entry.Key);
            dutch.Should().NotBe(english, entry.Key);
            entry.Sample.Should().NotBeNullOrWhiteSpace(entry.Key);
            entry
                .Contexts.Should()
                .BeEquivalentTo([TemplateHelperContext.Command, TemplateHelperContext.Pipeline]);

            string rendered = await fixture.RenderAsync(
                entry.Sample,
                PeopleHelperFixture.ChannelA,
                Seeds(Fixture)
            );

            rendered.Should().Be(entry.Sample, entry.Key);
        }
    }

    private static Dictionary<string, string> Seeds(string arguments)
    {
        Dictionary<string, string> seeds = PeopleHelperFixture.Seeds(target: null);
        foreach ((string key, string value) in ChatMessageHandler.BuildArgumentVariables(arguments))
            seeds[key] = value;
        return seeds;
    }
}
