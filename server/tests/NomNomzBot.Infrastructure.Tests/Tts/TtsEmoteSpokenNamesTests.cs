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
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// A channel's Twitch emotes all start with the channel prefix (an affiliate's is the first letters of the login
/// plus a collision number, so 0, 1 or 2 digits; a partner picks one or none). The voice should say the name of the
/// emote, not the prefix. The prefix is only removed when EVERY emote of the channel agrees on it: a shape rule
/// alone would cut "gachiGASM" (a partner's own name, no prefix) down to "GASM".
/// </summary>
public sealed class TtsEmoteSpokenNamesTests
{
    private static readonly SpokenNameFormatter Formatter = new();

    private static Dictionary<string, string> Derive(params string[] codes) =>
        TtsEmoteSpokenNames.Derive(codes, Formatter.Format);

    [Fact]
    public void A_two_digit_prefix_is_removed_and_the_name_is_spoken()
    {
        Dictionary<string, string> spoken = Derive("stoney90Hype", "stoney90Hug", "stoney90Lurk");

        spoken["stoney90Hype"].Should().Be("Hype");
        spoken["stoney90Lurk"].Should().Be("Lurk");
    }

    [Fact]
    public void A_one_digit_prefix_is_removed()
    {
        Derive("stoney9Hype", "stoney9Hug")["stoney9Hug"].Should().Be("Hug");
    }

    [Fact]
    public void A_prefix_without_digits_is_removed()
    {
        Derive("stoneyHype", "stoneyHug")["stoneyHype"].Should().Be("Hype");
    }

    [Fact]
    public void A_camel_case_name_is_spoken_as_separate_words()
    {
        Derive("stoney90HypeTrain", "stoney90Hug")["stoney90HypeTrain"].Should().Be("Hype Train");
    }

    [Fact]
    public void A_leetspeak_name_gets_the_same_treatment_as_a_viewer_name()
    {
        Dictionary<string, string> spoken = Derive("stoney90D4rkMode", "stoney90Hug");

        spoken["stoney90D4rkMode"].Should().Be(Formatter.Format("D4rkMode"));
        spoken["stoney90D4rkMode"].Should().NotContain("4");
    }

    [Fact]
    public void A_partner_with_no_prefix_keeps_every_emote_as_written()
    {
        Derive("Kappa", "LUL", "PogChamp").Should().BeEmpty();
    }

    [Fact]
    public void A_shared_theme_word_is_not_a_prefix_when_one_emote_does_not_have_it()
    {
        Derive("gachiGASM", "gachiBASS", "Kappa").Should().BeEmpty();
    }

    [Fact]
    public void One_emote_proves_nothing_so_it_is_kept_as_written()
    {
        Derive("stoney90Hype").Should().BeEmpty();
    }

    [Fact]
    public void A_prefix_that_ends_in_a_digit_is_not_stretched_into_a_suffix_that_starts_with_one()
    {
        Dictionary<string, string> spoken = Derive("stoney902Cool", "stoney90Hype");

        spoken["stoney90Hype"].Should().Be("Hype");
        spoken["stoney902Cool"].Should().Be(Formatter.Format("2Cool"));
    }

    [Fact]
    public void Lowercase_suffixes_give_no_boundary_so_nothing_is_removed()
    {
        Derive("stoney90hype", "stoney90hug").Should().BeEmpty();
    }

    [Fact]
    public void Codes_are_matched_with_their_exact_letter_case()
    {
        Derive("stoney90Hype", "stoney90Hug").Should().NotContainKey("STONEY90HYPE");
    }

    [Fact]
    public void An_emote_that_is_only_the_prefix_is_kept_as_written()
    {
        Derive("stoney90", "stoney90Hug").Should().BeEmpty();
    }
}
