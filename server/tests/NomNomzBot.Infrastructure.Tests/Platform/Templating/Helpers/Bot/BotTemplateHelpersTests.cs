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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Tests.Localization;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Bot;

public sealed class BotTemplateHelpersTests
{
    [Fact]
    public async Task The_scan_registers_one_bot_provider_that_owns_the_bot_keys()
    {
        await using BotHelperFixture fixture = new();

        ITemplateHelperProvider? provider = fixture.Registry.ProviderFor("bot.uptime");

        provider.Should().NotBeNull();
        provider!.Namespace.Should().Be("bot");
        fixture.Registry.ProviderFor("bot.commands.list.forUser").Should().BeSameAs(provider);
        fixture.Registry.ProviderFor("bot.nope").Should().BeNull();
    }

    [Fact]
    public async Task Every_entry_has_an_english_string_a_dutch_string_and_a_sample()
    {
        await using BotHelperFixture fixture = new();
        DashboardStringsXmlCatalog strings = new();
        List<TemplateHelperEntry> entries = BotEntries(fixture);

        entries.Should().HaveCount(21);
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
        await using BotHelperFixture fixture = new();

        foreach (TemplateHelperEntry entry in BotEntries(fixture))
        {
            string rendered = await fixture.RenderAsync(entry.Sample);

            rendered.Should().Be(entry.Sample, entry.Key);
        }
    }

    [Fact]
    public async Task A_viewer_sees_only_the_commands_the_role_allows_and_a_moderator_sees_them_all()
    {
        await using BotHelperFixture fixture = new();

        string viewer = await fixture.RenderAsync(
            "{bot.commands.list.forUser}",
            new() { ["user.role"] = "viewer" }
        );
        string unseeded = await fixture.RenderAsync("{bot.commands.list.forUser}", []);
        string moderator = await fixture.RenderAsync(
            "{bot.commands.list.forUser}",
            BotHelperFixture.Seeds("moderator")
        );

        viewer.Should().Be("!hug !lurk");
        unseeded.Should().Be("!hug !lurk");
        moderator.Should().Be("!hug !lurk !so !uptime !sr");
    }

    [Fact]
    public async Task A_command_is_listed_as_typed_once_with_its_own_prefix_and_never_by_alias_or_regex()
    {
        List<CachedCommand> commands =
        [
            BotHelperFixture.Command("hug", aliases: ["hugs", "cuddle"]),
            BotHelperFixture.Command("!Zed"),
            BotHelperFixture.Command("ban", level: 10, prefixMode: "None"),
            BotHelperFixture.Command("deal", prefixMode: "Custom", customPrefix: "?"),
            BotHelperFixture.Command("^hello", matchMode: "Regex"),
        ];
        await using BotHelperFixture fixture = new(new BotWorld(Commands: commands, Builtins: []));

        string all = await fixture.RenderAsync("{bot.commands.list}|{bot.commands.count}");
        string forViewer = await fixture.RenderAsync("{bot.commands.list.forUser}");

        all.Should().Be("ban ?deal !hug !Zed|4");
        forViewer.Should().Be("?deal !hug !Zed");
    }

    [Fact]
    public async Task A_built_in_that_is_off_for_the_channel_is_not_listed_and_the_channel_floor_wins()
    {
        List<BuiltinCommandDto> builtins =
        [
            BotHelperFixture.Builtin("so", "Moderator", enabled: false),
            BotHelperFixture.Builtin("sr", "Subscriber", channelFloor: "Everyone"),
        ];
        await using BotHelperFixture fixture = new(new BotWorld(Builtins: builtins));

        string rendered = await fixture.RenderAsync(
            "{bot.commands.list.builtin}|{bot.commands.list.forUser}",
            BotHelperFixture.Seeds("viewer")
        );

        rendered.Should().Be("!sr|!hug !lurk !sr");
    }

    [Fact]
    public async Task A_bot_that_is_not_a_moderator_says_false_and_a_bot_never_observed_says_nothing()
    {
        await using BotHelperFixture notMod = new(
            new BotWorld(Standing: BotModeratorStanding.NotModerator)
        );
        await using BotHelperFixture unknown = new(
            new BotWorld(Standing: BotModeratorStanding.NotObserved)
        );
        await using BotHelperFixture own = new(
            new BotWorld(Standing: BotModeratorStanding.NoDedicatedBot)
        );
        const string template = "[{bot.isMod}]{if:{bot.isMod}|can|cannot}";

        (await notMod.RenderAsync(template)).Should().Be("[false]cannot");
        (await unknown.RenderAsync(template)).Should().Be("[]cannot");
        (await own.RenderAsync(template)).Should().Be("[true]can");
    }

    [Fact]
    public async Task A_read_that_fails_prints_nothing_instead_of_a_partial_list_or_a_made_up_number()
    {
        await using BotHelperFixture fixture = new(
            new BotWorld(BuiltinsFail: true, TimersFail: true)
        );

        string rendered = await fixture.RenderAsync(
            "[{bot.commands.list}][{bot.commands.list.builtin}][{bot.commands.list.forUser}]"
                + "[{bot.commands.count}][{bot.timers.count}][{bot.commands.list.custom}]"
        );

        rendered.Should().Be("[][][][][][!hug !lurk]");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost:5080")]
    [InlineData("not a url")]
    public async Task The_dashboard_link_needs_a_public_address_and_prints_nothing_without_one(
        string? baseUrl
    )
    {
        await using BotHelperFixture fixture = new(new BotWorld(BaseUrl: baseUrl));

        (await fixture.RenderAsync("[{bot.dashboard}]")).Should().Be("[]");
    }

    [Fact]
    public async Task The_dashboard_link_drops_a_trailing_slash_of_the_configured_address()
    {
        await using BotHelperFixture fixture = new(
            new BotWorld(BaseUrl: "https://bot.example.org/")
        );

        (await fixture.RenderAsync("{bot.dashboard}"))
            .Should()
            .Be("https://bot.example.org/app/u/samplechannel");
    }

    [Fact]
    public async Task A_channel_in_dutch_gets_dutch_words_and_a_language_that_is_unset_reads_as_english()
    {
        await using BotHelperFixture dutch = new(new BotWorld(Language: "nl"));
        await using BotHelperFixture unset = new(new BotWorld(Language: null));

        (await dutch.RenderAsync("{bot.uptime.long}|{bot.lastRestart.relative}|{bot.language}"))
            .Should()
            .Be("3 dagen, 4 uur|3 dagen geleden|nl");
        (await unset.RenderAsync("{bot.uptime.long}|{bot.language}"))
            .Should()
            .Be("3 days, 4 hours|en");
    }

    [Fact]
    public async Task A_form_given_an_argument_prints_the_token_as_written()
    {
        await using BotHelperFixture fixture = new();

        (await fixture.RenderAsync("{bot.name:x} {bot.uptime:1}"))
            .Should()
            .Be("{bot.name:x} {bot.uptime:1}");
    }

    [Fact]
    public async Task Many_bot_tokens_in_one_render_read_each_service_once()
    {
        await using BotHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{bot.name} {bot.mention} {bot.commands.list} {bot.commands.list.builtin} "
                + "{bot.commands.list.forUser} {bot.commands.count} {bot.timers.count} "
                + "{bot.timers.count} {bot.isMod} {bot.dashboard}"
        );

        rendered
            .Should()
            .Be(
                "SampleBot @SampleBot !hug !lurk !so !uptime !sr !so !uptime !sr !hug !lurk !sr 5 5 5 true "
                    + "https://nomnomz.bot/app/u/samplechannel"
            );
        await fixture
            .Identity.Received(1)
            .NameAsync(Arg.Any<IServiceProvider>(), Arg.Any<CancellationToken>());
        await fixture
            .Builtins.Received(1)
            .ListAsync(BotHelperFixture.ChannelA.ToString(), Arg.Any<CancellationToken>());
        await fixture
            .Timers.Received(1)
            .ListAsync(
                BotHelperFixture.ChannelA.ToString(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            );
        await fixture
            .Moderator.Received(1)
            .ReadAsync(BotHelperFixture.ChannelA, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_render_that_reads_only_the_process_or_the_cache_calls_no_service()
    {
        await using BotHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{bot.version} {bot.uptime} {bot.prefix} {bot.commands.list.custom} {text.upper:world}"
        );

        rendered.Should().Be("1.42.0 3d 4h ! !hug !lurk WORLD");
        await fixture.Identity.DidNotReceiveWithAnyArgs().NameAsync(default!, default);
        await fixture.Builtins.DidNotReceiveWithAnyArgs().ListAsync(default!, default);
        await fixture.Timers.DidNotReceiveWithAnyArgs().ListAsync(default!, default!, default);
        await fixture.Moderator.DidNotReceiveWithAnyArgs().ReadAsync(default, default);
    }

    private static List<TemplateHelperEntry> BotEntries(BotHelperFixture fixture) =>
        [
            .. fixture.Registry.All.Where(e =>
                fixture.Registry.ProviderFor(e.Key) is { Namespace: "bot" }
            ),
        ];
}
