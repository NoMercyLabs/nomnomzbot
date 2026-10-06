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
using NomNomzBot.Infrastructure.Tts;

namespace NomNomzBot.Infrastructure.Tests.Tts;

public sealed class SpokenNameFormatterTests
{
    private readonly SpokenNameFormatter _sut = new();

    [Theory]
    [InlineData("N00bSl4y3r", "Noob Slayer")]
    [InlineData("xX_D4rk_Xx", "Dark")]
    [InlineData("xXD4rkXx", "Dark")]
    [InlineData("Stoney_Eagle", "Stoney Eagle")]
    [InlineData("gamer123", "gamer 123")]
    [InlineData("kani_dev", "kani dev")]
    [InlineData("kani-dev", "kani dev")]
    [InlineData("kani.dev", "kani dev")]
    [InlineData("NoMercyBot", "No Mercy Bot")]
    [InlineData("TTSBot", "TTS Bot")]
    [InlineData("12345", "12345")]
    [InlineData("1337", "1337")]
    [InlineData("Sl@yer", "Slayer")]
    [InlineData("Ca$h", "Cash")]
    [InlineData("L1ght", "Light")]
    [InlineData("Pro2Win", "Pro 2 Win")]
    [InlineData("__Quiet__", "Quiet")]
    [InlineData("~~~Wave~~~", "Wave")]
    [InlineData("@Stoney_Eagle", "Stoney Eagle")]
    [InlineData("Max", "Max")]
    [InlineData("MaxX", "Max X")]
    [InlineData("Star🔥Fire", "Star Fire")]
    [InlineData("Dark1", "Dark 1")]
    [InlineData("Player1Gamer", "Player 1 Gamer")]
    [InlineData("Team4Life", "Team 4 Life")]
    [InlineData("L33T", "LEET")]
    [InlineData("H4X0R", "HAXOR")]
    [InlineData("h4x0r", "haxor")]
    public void Format_GivesTheSpokenForm(string name, string expected) =>
        _sut.Format(name).Should().Be(expected);

    [Theory]
    [InlineData("xX_Xx")]
    [InlineData("___")]
    [InlineData("🔥🔥")]
    public void Format_NeverReturnsEmpty_ItFallsBackToTheOriginal(string name) =>
        _sut.Format(name).Should().Be(name);

    [Fact]
    public void ApplyToText_CleansKnownNamesAndMentions_AndLeavesBareWordsAlone()
    {
        string spoken = _sut.ApplyToText(
            "thanks xX_D4rk_Xx and @N00bSl4y3r, also Stoney_Eagle_fan_2 and gamer123 said hi",
            ["xX_D4rk_Xx"]
        );

        spoken
            .Should()
            .Be("thanks Dark and Noob Slayer, also Stoney_Eagle_fan_2 and gamer123 said hi");
    }

    [Fact]
    public void ApplyToText_MatchesWholeNamesOnly_CaseInsensitively()
    {
        _sut.ApplyToText("kani_dev and kani_devs and KANI_DEV", ["kani_dev"])
            .Should()
            .Be("kani dev and kani_devs and KANI DEV");
    }

    [Fact]
    public void ApplyToText_LeavesAnEmailAlone()
    {
        _sut.ApplyToText("mail me at a@b_c.com", []).Should().Be("mail me at a@b_c.com");
    }
}
