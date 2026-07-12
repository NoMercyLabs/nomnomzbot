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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// Proves the personality precedence ladder the whole built-in surface phrases itself through
/// (<see cref="BuiltinResponseComposer"/>): an explicit per-command override wins over the tone template,
/// the tone template wins over the built-in's neutral fallback, and every winner is rendered with the
/// built-in's real variables. This is the behavioral heart of the tone system.
/// </summary>
public sealed class BuiltinResponseComposerTests
{
    private static readonly Guid Channel = Guid.Parse("0198c000-0000-7000-8000-0000000000f1");

    /// <summary>A resolver double that performs literal <c>{key}</c> substitution — no DB/registry needed.</summary>
    private static ITemplateResolver FakeResolver()
    {
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string template = call.ArgAt<string>(0);
                foreach (
                    KeyValuePair<string, string> kvp in call.ArgAt<IDictionary<string, string>>(1)
                )
                    template = template.Replace($"{{{kvp.Key}}}", kvp.Value);
                return Task.FromResult(template);
            });
        return resolver;
    }

    private static BuiltinResponseComposer Sut() => new(FakeResolver());

    [Fact]
    public async Task Override_wins_over_the_tone_template_and_is_rendered_with_the_variables()
    {
        // Sassy uptime HAS tone templates, but an explicit override must beat them.
        string result = await Sut()
            .ComposeAsync(
                new BuiltinResponseRequest
                {
                    BroadcasterId = Channel,
                    Personality = PersonalityTone.Sassy,
                    BuiltinKey = BuiltinResponseSlots.Uptime.Key,
                    Slot = BuiltinResponseSlots.Uptime.Live,
                    OverrideTemplate = "MY channel has been live {uptime}, deal with it.",
                    NeutralFallback = "The stream has been live for {uptime}.",
                    Variables = new Dictionary<string, string> { ["uptime"] = "2h 5m" },
                }
            );

        result.Should().Be("MY channel has been live 2h 5m, deal with it.");
        IReadOnlyList<string> sassy = ToneTemplateCatalog.Get(
            PersonalityTone.Sassy,
            BuiltinResponseSlots.Uptime.Key,
            BuiltinResponseSlots.Uptime.Live
        );
        sassy.Should().NotBeEmpty("the override must have beaten a real, populated tone set");
    }

    [Fact]
    public async Task Tone_template_wins_over_the_neutral_fallback_when_no_override()
    {
        string result = await Sut()
            .ComposeAsync(
                new BuiltinResponseRequest
                {
                    BroadcasterId = Channel,
                    Personality = PersonalityTone.Sassy,
                    BuiltinKey = BuiltinResponseSlots.Uptime.Key,
                    Slot = BuiltinResponseSlots.Uptime.Live,
                    OverrideTemplate = null,
                    NeutralFallback = "NEUTRAL {uptime}",
                    Variables = new Dictionary<string, string> { ["uptime"] = "2h 5m" },
                }
            );

        // Must be one of the Sassy variations (rendered), never the neutral fallback.
        HashSet<string> expected = ToneTemplateCatalog
            .Get(
                PersonalityTone.Sassy,
                BuiltinResponseSlots.Uptime.Key,
                BuiltinResponseSlots.Uptime.Live
            )
            .Select(t => t.Replace("{uptime}", "2h 5m"))
            .ToHashSet();

        result.Should().NotBe("NEUTRAL 2h 5m");
        expected.Should().Contain(result);
    }

    [Fact]
    public async Task Neutral_fallback_is_used_when_the_tone_has_no_template_for_the_slot()
    {
        // stats/profile is authored ONLY for the flavored tones — Informative deliberately has none, so the
        // default tone falls through to the built-in's neutral line.
        ToneTemplateCatalog
            .Get(
                PersonalityTone.Informative,
                BuiltinResponseSlots.Stats.Key,
                BuiltinResponseSlots.Stats.Profile
            )
            .Should()
            .BeEmpty("Informative is intentionally omitted for !stats");

        string result = await Sut()
            .ComposeAsync(
                new BuiltinResponseRequest
                {
                    BroadcasterId = Channel,
                    Personality = PersonalityTone.Informative,
                    BuiltinKey = BuiltinResponseSlots.Stats.Key,
                    Slot = BuiltinResponseSlots.Stats.Profile,
                    NeutralFallback = "Alice: 42 messages, 500 points.",
                }
            );

        result.Should().Be("Alice: 42 messages, 500 points.");
    }

    [Fact]
    public async Task An_unknown_slot_falls_back_to_the_neutral_string()
    {
        string result = await Sut()
            .ComposeAsync(
                new BuiltinResponseRequest
                {
                    BroadcasterId = Channel,
                    Personality = PersonalityTone.Hype,
                    BuiltinKey = "uptime",
                    Slot = "not-a-real-slot",
                    NeutralFallback = "fallback text",
                }
            );

        result.Should().Be("fallback text");
    }

    [Fact]
    public async Task A_blank_override_does_not_win_over_the_tone_template()
    {
        // Whitespace/empty override is treated as "no override" — the tone must still apply.
        string result = await Sut()
            .ComposeAsync(
                new BuiltinResponseRequest
                {
                    BroadcasterId = Channel,
                    Personality = PersonalityTone.Chill,
                    BuiltinKey = BuiltinResponseSlots.Song.Key,
                    Slot = BuiltinResponseSlots.Song.Nothing,
                    OverrideTemplate = "   ",
                    NeutralFallback = "NEUTRAL",
                }
            );

        HashSet<string> chill = ToneTemplateCatalog
            .Get(
                PersonalityTone.Chill,
                BuiltinResponseSlots.Song.Key,
                BuiltinResponseSlots.Song.Nothing
            )
            .ToHashSet();

        result.Should().NotBe("NEUTRAL");
        chill.Should().Contain(result);
    }
}
