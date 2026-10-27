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
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;
using NomNomzBot.Infrastructure.Tests.Localization;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.People;

public sealed class PeopleTemplateHelpersTests
{
    [Fact]
    public async Task Two_channels_never_mix_the_values_of_the_same_person()
    {
        await using PeopleHelperFixture fixture = new();
        const string template =
            "{user.points} {user.points.name}|{user.points.rank}|{user.stats.streak}|{user.quotes.count}|{user.stats.bits}";

        string inA = await fixture.RenderAsync(template, PeopleHelperFixture.ChannelA);
        string inB = await fixture.RenderAsync(template, PeopleHelperFixture.ChannelB);

        inA.Should().Be("2,500 nomz|4|5|3|2,300");
        inB.Should().Be("90 coins|2|1|1|10");
    }

    [Fact]
    public async Task A_person_with_a_record_in_one_channel_only_reads_as_unknown_in_the_other()
    {
        await using PeopleHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{target.points}|{target.stats.lastSeen}|{target.stats.streak}|{target.stats.bits}",
            PeopleHelperFixture.ChannelB
        );

        rendered.Should().Be("0||0|0");
    }

    [Fact]
    public async Task A_person_without_a_record_reads_as_zeros_and_empty_text_never_invented_values()
    {
        await using PeopleHelperFixture fixture = new();
        Dictionary<string, string> seeds = PeopleHelperFixture.Seeds("@Ghost");

        string rendered = await fixture.RenderAsync(
            "{target.mention}|{target.points}|{target.points.rank}|{target.stats.lastSeen}|{target.stats.streak}|{target.stats.bits}|{target.avatar}|{target.game}|{target.isFollower}|{target.isSub}|{target.isLive}",
            PeopleHelperFixture.ChannelA,
            seeds
        );

        rendered.Should().Be("@Ghost|0|||0|0|||false|false|false");
    }

    [Fact]
    public async Task A_command_without_a_mention_leaves_every_target_token_as_written()
    {
        await using PeopleHelperFixture fixture = new();
        Dictionary<string, string> seeds = PeopleHelperFixture.Seeds(target: null);

        string rendered = await fixture.RenderAsync(
            "{target.points} {target.mention}",
            PeopleHelperFixture.ChannelA,
            seeds
        );

        rendered.Should().Be("{target.points} {target.mention}");
    }

    [Fact]
    public async Task A_render_without_a_channel_leaves_a_viewer_token_as_written()
    {
        await using PeopleHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("{user.points}", broadcasterId: null);

        rendered.Should().Be("{user.points}");
    }

    [Fact]
    public async Task A_seed_of_the_same_key_wins_over_the_row()
    {
        await using PeopleHelperFixture fixture = new();
        Dictionary<string, string> seeds = PeopleHelperFixture.Seeds();
        seeds["target.isLive"] = "true";

        string rendered = await fixture.RenderAsync(
            "{target.isLive}",
            PeopleHelperFixture.ChannelA,
            seeds
        );

        rendered.Should().Be("true");
    }

    [Fact]
    public async Task The_providers_claim_only_their_own_keys_and_never_a_legacy_key()
    {
        await using PeopleHelperFixture fixture = new();
        ITemplateHelperRegistry registry = fixture.Registry;

        registry.ProviderFor("user.points").Should().BeOfType<UserTemplateHelpers>();
        registry.ProviderFor("user.data.color").Should().BeOfType<UserTemplateHelpers>();
        registry.ProviderFor("target.points").Should().BeOfType<TargetTemplateHelpers>();
        foreach (
            string legacy in new[]
            {
                "user",
                "user.name",
                "user.id",
                "user.provider",
                "user.accountAge",
                "user.followAge",
                "user.pronouns",
                "user.messageCount",
                "user.lastmessage",
                "user.link",
                "user.subject",
                "user.tense",
                "user.verb:s|",
                "target.name",
                "target.displayname",
                "target.known",
                "target.id",
                "target.followAge",
                "target.lastmessage",
                "target.link",
                "target.subject",
                "target.data.color",
                "target.messages",
                "viewer.messages",
                "viewer.data.color",
            }
        )
            registry.ProviderFor(legacy).Should().BeNull(legacy);
    }

    [Fact]
    public async Task Every_user_row_has_a_target_twin_or_is_a_user_only_form()
    {
        await using PeopleHelperFixture fixture = new();

        IEnumerable<string> userKeys = fixture
            .Registry.All.Where(e =>
                e.Scope == TemplateHelperScope.Viewer
                && e.Key.StartsWith("user.", StringComparison.Ordinal)
            )
            .Select(e => e.Key);
        IEnumerable<string> targetKeys = fixture
            .Registry.All.Where(e => e.Scope == TemplateHelperScope.Target)
            .Select(e => e.Key);

        userKeys.Should().Contain("user.points").And.Contain("user.stats.streak");
        targetKeys.Should().Contain("target.points").And.Contain("target.stats.streak");
        targetKeys.Should().OnlyContain(k => k.StartsWith("target.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Every_entry_has_an_english_string_a_dutch_string_and_a_sample()
    {
        await using PeopleHelperFixture fixture = new();
        DashboardStringsXmlCatalog strings = new();
        List<TemplateHelperEntry> entries =
        [
            .. fixture.Registry.All.Where(e =>
                e.Scope is TemplateHelperScope.Viewer or TemplateHelperScope.Target
            ),
        ];

        entries.Should().HaveCountGreaterThan(50);
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
        }
    }

    [Fact]
    public async Task Every_sample_is_plain_text_that_renders_to_itself_through_the_resolver()
    {
        await using PeopleHelperFixture fixture = new();

        foreach (
            TemplateHelperEntry entry in fixture.Registry.All.Where(e =>
                e.Scope is TemplateHelperScope.Viewer or TemplateHelperScope.Target
                && e.Prefix is null
            )
        )
        {
            string rendered = await fixture.RenderAsync(entry.Sample, PeopleHelperFixture.ChannelA);

            rendered.Should().Be(entry.Sample, entry.Key);
        }
    }
}
