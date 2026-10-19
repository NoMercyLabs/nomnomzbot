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
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Tests.Commands;

/// <summary>
/// A viewer refused as "not music" must be told the rule, in every mood: YouTube requests must be in
/// YouTube's Music category or come from an official artist (- Topic) channel, and what to send instead.
/// (Owner, 2026-10-19: "it needs to say that the music can only come from channels marked as artists or
/// something that makes it super clear in every mood reply".)
/// </summary>
public sealed class NotMusicReplyReasonTests
{
    private const string RulePattern =
        @"youtube requests (must|have to) be in youtube's music category or come from an official artist \(- topic\) channel";

    public static TheoryData<string> Tones =>
        new()
        {
            PersonalityTone.Informative,
            PersonalityTone.Friendly,
            PersonalityTone.Sassy,
            PersonalityTone.Hype,
            PersonalityTone.Chill,
        };

    [Theory]
    [MemberData(nameof(Tones))]
    public void Every_mood_variant_states_the_rule_and_what_to_send_instead(string tone)
    {
        IReadOnlyList<string> variants = ToneTemplateCatalog.Get(
            tone,
            BuiltinResponseSlots.SongRequest.Key,
            BuiltinResponseSlots.SongRequest.NotMusic
        );

        variants.Should().NotBeEmpty();
        foreach (string variant in variants)
        {
            string text = variant.ToLowerInvariant();
            text.Should().MatchRegex(RulePattern, variant);
            text.Should().Contain("youtube music link", variant);
            text.Should().Contain("song name", variant);
        }
    }
}
