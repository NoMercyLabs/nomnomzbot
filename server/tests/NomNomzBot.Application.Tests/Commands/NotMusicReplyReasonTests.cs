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

    public static TheoryData<string> Tones => new(PersonalityTone.All);

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
