// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Tests.Commands;

/// <summary>
/// Proves the content contract of <see cref="EventResponseToneCatalog"/>: every event ships all five tones
/// with 1–4 lines, no tone ever uses a placeholder its Informative lines lack (so a tone can never print a
/// raw <c>{token}</c>), the voices hold (Hype uppercase, Chill lowercase), and picking never repeats a line
/// back to back.
/// </summary>
public sealed partial class EventResponseToneCatalogTests
{
    [GeneratedRegex(@"\{[^{}]+\}")]
    private static partial Regex Placeholder();

    private static HashSet<string> PlaceholdersOf(IEnumerable<string> lines) =>
        [.. lines.SelectMany(l => Placeholder().Matches(l).Select(m => m.Value))];

    public static TheoryData<string> Events() => [.. EventResponseToneCatalog.EventTypes];

    [Fact]
    public void The_catalogue_covers_the_nine_events_that_ship_on_plus_the_ad_break_and_all_are_real_event_types()
    {
        EventResponseToneCatalog
            .EventTypes.Should()
            .HaveCount(10)
            .And.Contain("channel.ad_break.begin");
        EventResponseToneCatalog
            .EventTypes.Should()
            .OnlyContain(e => EventResponsePresetCatalog.EventTypes.Contains(e));
    }

    [Theory]
    [MemberData(nameof(Events))]
    public void Every_event_has_one_to_four_lines_in_each_of_the_five_tones(string eventType)
    {
        foreach (string tone in PersonalityTone.All)
            EventResponseToneCatalog
                .Get(tone, eventType)
                .Should()
                .HaveCountGreaterThanOrEqualTo(1, $"{eventType}/{tone}")
                .And.HaveCountLessThanOrEqualTo(4, $"{eventType}/{tone}");
    }

    [Theory]
    [MemberData(nameof(Events))]
    public void Every_tone_uses_only_placeholders_the_informative_lines_use(string eventType)
    {
        HashSet<string> allowed = PlaceholdersOf(
            EventResponseToneCatalog.Get(PersonalityTone.Informative, eventType)
        );

        foreach (string tone in PersonalityTone.All)
            PlaceholdersOf(EventResponseToneCatalog.Get(tone, eventType))
                .Should()
                .BeSubsetOf(allowed, $"{eventType}/{tone} must not introduce a placeholder");
    }

    [Theory]
    [MemberData(nameof(Events))]
    public void Every_event_line_that_carries_also_said_ends_with_it(string eventType)
    {
        foreach (string tone in PersonalityTone.All)
        foreach (string line in EventResponseToneCatalog.Get(tone, eventType))
            if (line.Contains("{also_said}", StringComparison.Ordinal))
                line.Should()
                    .EndWith("{also_said}", "the clause starts with a space and closes the line");
    }

    [Theory]
    [MemberData(nameof(Events))]
    public void Hype_lines_are_uppercase_and_chill_lines_are_lowercase(string eventType)
    {
        foreach (string line in EventResponseToneCatalog.Get(PersonalityTone.Hype, eventType))
            Placeholder()
                .Replace(line, string.Empty)
                .Should()
                .Be(Placeholder().Replace(line, string.Empty).ToUpperInvariant());
        foreach (string line in EventResponseToneCatalog.Get(PersonalityTone.Chill, eventType))
            Placeholder()
                .Replace(line, string.Empty)
                .Should()
                .Be(Placeholder().Replace(line, string.Empty).ToLowerInvariant());
    }

    [Theory]
    [MemberData(nameof(Events))]
    public void A_picked_line_is_never_the_one_picked_last_time(string eventType)
    {
        string? previous = EventResponseToneCatalog.Pick(PersonalityTone.Sassy, eventType);

        for (int i = 0; i < 100; i++)
        {
            string? next = EventResponseToneCatalog.Pick(PersonalityTone.Sassy, eventType);
            next.Should().NotBeNull().And.NotBe(previous);
            previous = next;
        }
    }

    [Fact]
    public void An_event_outside_the_catalogue_has_no_lines_and_nothing_to_pick()
    {
        EventResponseToneCatalog.Get(PersonalityTone.Sassy, "stream.online").Should().BeEmpty();
        EventResponseToneCatalog.Pick(PersonalityTone.Sassy, "stream.online").Should().BeNull();
        EventResponseToneCatalog.FirstInformative("stream.online").Should().BeNull();
    }

    [Fact]
    public void An_unknown_or_blank_tone_falls_back_to_the_informative_lines()
    {
        IReadOnlyList<string> informative = EventResponseToneCatalog.Get(
            PersonalityTone.Informative,
            "channel.follow"
        );

        EventResponseToneCatalog.Get(null, "channel.follow").Should().Equal(informative);
        EventResponseToneCatalog.Get("robotic", "channel.follow").Should().Equal(informative);
    }

    [Fact]
    public void Admin_text_replaces_the_tone_lines_and_blank_admin_text_does_not()
    {
        EventResponseToneCatalog
            .FollowingLines("Thanks {user}!", PersonalityTone.Sassy, "channel.follow")
            .Should()
            .Equal("Thanks {user}!");
        EventResponseToneCatalog
            .FollowingLines("  ", PersonalityTone.Sassy, "channel.follow")
            .Should()
            .Equal(EventResponseToneCatalog.Get(PersonalityTone.Sassy, "channel.follow"));
    }
}
