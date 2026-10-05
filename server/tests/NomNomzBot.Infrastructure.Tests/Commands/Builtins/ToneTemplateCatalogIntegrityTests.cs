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
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Platform.Templating;

namespace NomNomzBot.Infrastructure.Tests.Commands.Builtins;

/// <summary>
/// The reply catalogue (commands-pipelines.md §11) is what a streamer edits, so every shipped line must pass the
/// SAME save-time check a streamer's own text does: every placeholder is a declared slot variable or a registered
/// template helper. A shipped line that names an undeclared variable would render a raw "{x}" in chat — and would
/// be un-saveable if the streamer copied it into the editor.
/// </summary>
public sealed class ToneTemplateCatalogIntegrityTests
{
    private static readonly string[] Tones =
    [
        PersonalityTone.Informative,
        PersonalityTone.Friendly,
        PersonalityTone.Sassy,
        PersonalityTone.Hype,
        PersonalityTone.Chill,
    ];

    [Fact]
    public void Every_shipped_line_uses_only_its_slots_declared_variables_or_registered_helpers()
    {
        TemplateHelperValidator validator = new();
        List<string> failures = [];

        foreach ((string key, string slot) in ToneTemplateCatalog.AllSlots())
        {
            IReadOnlyList<string> declared = ToneTemplateCatalog.Variables(key, slot);
            foreach (string tone in Tones)
            foreach (string line in ToneTemplateCatalog.Get(tone, key, slot))
            {
                Result valid = validator.Validate(line, TemplateHelperContext.Command, declared);
                if (valid.IsFailure)
                    failures.Add($"{key}/{slot} ({tone}): {valid.ErrorMessage}");
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void The_sassy_shoutout_announcement_carries_the_old_bots_nine_lines_with_the_target_name()
    {
        IReadOnlyList<string> lines = ToneTemplateCatalog.Get(
            PersonalityTone.Sassy,
            BuiltinResponseSlots.Shoutout.Key,
            BuiltinResponseSlots.Shoutout.Announcement
        );

        lines.Should().HaveCount(9);
        lines.Should().OnlyContain(l => l.Contains("{target.name}"));
        foreach (string legacy in new[] { "{game}", "{displayname}", "{tense}", "{presentTense}" })
            lines.Should().NotContain(l => l.Contains(legacy));
    }

    [Fact]
    public void The_sassy_duplicate_song_pool_carries_the_old_bots_ten_lines_with_the_user_name()
    {
        IReadOnlyList<string> lines = ToneTemplateCatalog.Get(
            PersonalityTone.Sassy,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.Duplicate
        );

        lines
            .Should()
            .Contain(
                "Congratulations @{user}! That song is already in the queue. Maybe check next time?"
            )
            .And.Contain("Achievement unlocked @{user}: 'Didn't Check the Queue!'")
            .And.Contain(
                "Fun fact @{user}: That song is already queued! Less fun fact: You didn't check first."
            );
        lines.Count(l => l.Contains("@{user}")).Should().Be(10);
        lines.Should().NotContain(l => l.Contains("{name}"));
    }

    [Fact]
    public void A_too_long_track_refusal_has_a_line_in_every_tone_and_the_refunded_one_says_so()
    {
        foreach (string tone in Tones)
        {
            ToneTemplateCatalog
                .Get(
                    tone,
                    BuiltinResponseSlots.SongRequest.Key,
                    BuiltinResponseSlots.SongRequest.TrackTooLong
                )
                .Should()
                .NotBeEmpty($"{tone} needs its own too-long line");
            ToneTemplateCatalog
                .Get(
                    tone,
                    BuiltinResponseSlots.SongRequest.Key,
                    BuiltinResponseSlots.SongRequest.TrackTooLongRefunded
                )
                .Should()
                .NotBeEmpty($"{tone} needs its own refunded too-long line");
        }

        ToneTemplateCatalog
            .Get(
                PersonalityTone.Informative,
                BuiltinResponseSlots.SongRequest.Key,
                BuiltinResponseSlots.SongRequest.TrackTooLongRefunded
            )
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("10 minutes")
            .And.EndWith(", your point has been refunded.");
    }

    [Fact]
    public void Every_slot_ships_a_default_line_the_editor_can_show_and_reset_to()
    {
        List<string> missing =
        [
            .. ToneTemplateCatalog
                .AllSlots()
                .Where(s =>
                    string.IsNullOrWhiteSpace(
                        ToneTemplateCatalog.ShippedTemplate(s.BuiltinKey, s.Slot)
                    )
                )
                .Select(s => $"{s.BuiltinKey}/{s.Slot}"),
        ];

        missing.Should().BeEmpty("a reset must land on real text, never on an empty reply");
    }

    [Fact]
    public void Every_declared_variable_has_a_preview_sample()
    {
        List<string> missing =
        [
            .. ToneTemplateCatalog
                .AllSlots()
                .SelectMany(s => ToneTemplateCatalog.Variables(s.BuiltinKey, s.Slot))
                .Distinct()
                .Where(name => !HasSample(name)),
        ];

        missing.Should().BeEmpty("the editor's live preview fills every variable with its sample");
    }

    // queue.more is legitimately empty (nothing more waiting) — its sample is the empty string by design.
    private static bool HasSample(string name) =>
        name == "queue.more" || ToneTemplateCatalog.SampleValue(name).Length > 0;
}
