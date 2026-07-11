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
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the light TTS profanity mask (tts.md §3.5): a listed swear is masked to first-char + asterisks, matching
/// is case-insensitive and whole-word (so ordinary words that merely CONTAIN a swear as a substring are left
/// alone — no Scunthorpe false positives), clean text is returned unchanged with <c>WasCensored=false</c>, and
/// multiple hits in one string are all masked. This is the whole behavioural contract the dispatch relies on.
/// </summary>
public sealed class TtsProfanityCensorTests
{
    private readonly ITtsProfanityCensor _censor = new TtsProfanityCensor();

    [Theory]
    [InlineData("you piece of shit", "you piece of s***")]
    [InlineData("this is crap", "this is c***")]
    [InlineData("what a bastard", "what a b******")]
    public void Censor_MasksListedSwear_KeepingFirstChar(string input, string expected)
    {
        TtsCensorResult result = _censor.Censor(input);

        result.Text.Should().Be(expected);
        result.WasCensored.Should().BeTrue();
    }

    [Fact]
    public void Censor_IsCaseInsensitive_AndPreservesFirstCharCase()
    {
        TtsCensorResult result = _censor.Censor("SHIT and Crap");

        // First char case preserved, rest masked; both hits caught regardless of case.
        result.Text.Should().Be("S*** and C***");
        result.WasCensored.Should().BeTrue();
    }

    [Theory]
    [InlineData("this is a first-class assessment")] // "ass" is a substring of class/assessment — must NOT match
    [InlineData("pass the glasses")]
    [InlineData("a cocktail by the dock")] // "cock" substring of cocktail/dock
    public void Censor_DoesNotMatchSubstrings(string clean)
    {
        TtsCensorResult result = _censor.Censor(clean);

        result.Text.Should().Be(clean);
        result.WasCensored.Should().BeFalse();
    }

    [Fact]
    public void Censor_CleanText_ReturnsUnchanged()
    {
        TtsCensorResult result = _censor.Censor("hello world, welcome to the stream");

        result.Text.Should().Be("hello world, welcome to the stream");
        result.WasCensored.Should().BeFalse();
    }

    [Fact]
    public void Censor_MasksEveryHit_InAMultiSwearString()
    {
        TtsCensorResult result = _censor.Censor("shit crap piss");

        result.Text.Should().Be("s*** c*** p***");
        result.WasCensored.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Censor_EmptyOrWhitespace_ReturnsUnchangedUncensored(string input)
    {
        TtsCensorResult result = _censor.Censor(input);

        result.Text.Should().Be(input);
        result.WasCensored.Should().BeFalse();
    }
}
