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
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Domain.Tests.Moderation.SpamDefense;

/// <summary>
/// The capability floor (spam-defense.md §L4): what a message does that its sender's tier must have
/// earned. The risk it guards is false positives, so the negative cases carry most of the weight.
/// </summary>
public class CapabilityGateTests
{
    [Theory]
    [InlineData("watch this https://example.com/clip")]
    [InlineData("http://example.com")]
    [InlineData("see www.example.com for details")]
    [InlineData("example.com/path")]
    [InlineData("free stuff at shop.xyz")]
    public void ALinkIsDetected(string text) =>
        CapabilityGate.Detect(text).Should().Contain(SpamCapability.PostLink);

    [Theory]
    [InlineData("hello chat")]
    [InlineData("e.g. this works i.e. fine")]
    [InlineData("great stream... see you tomorrow")]
    [InlineData("v1.2 patch notes are out")]
    [InlineData("score was 3.5 today")]
    public void OrdinaryChat_IsNotMistakenForALink(string text) =>
        CapabilityGate.Detect(text).Should().NotContain(SpamCapability.PostLink);

    [Theory]
    [InlineData("こんにちは みなさん")]
    [InlineData("привет всем")]
    [InlineData("مرحبا بالجميع")]
    public void AMessageWrittenInAnotherScript_UsesTheNonLatinCapability(string text) =>
        CapabilityGate.Detect(text).Should().Contain(SpamCapability.NonLatinScript);

    [Theory]
    [InlineData("hello chat")]
    [InlineData("café résumé naïve")]
    [InlineData("xin chào các bạn ơi")]
    [InlineData("hello 你")] // mostly Latin letters: not a message written in another script
    [InlineData("12345 :) !!!")]
    public void LatinChat_DoesNotUseTheNonLatinCapability(string text) =>
        CapabilityGate.Detect(text).Should().NotContain(SpamCapability.NonLatinScript);

    [Fact]
    public void TheNonLatinFloor_IsRegular_WhenTheGateIsOn_AndUntrusted_WhenOff()
    {
        CapabilityGate
            .FloorsFor(nonLatinScriptGate: true)[SpamCapability.NonLatinScript]
            .Should()
            .Be(SpamTrustTier.Regular);
        CapabilityGate
            .FloorsFor(nonLatinScriptGate: false)[SpamCapability.NonLatinScript]
            .Should()
            .Be(SpamTrustTier.Untrusted);
        CapabilityGate
            .FloorsFor(nonLatinScriptGate: false)[SpamCapability.PostLink]
            .Should()
            .Be(SpamTrustTier.Known, "the other floors are the shipped ones");
    }

    [Fact]
    public void ALinkIsUnearned_BelowKnown_AndEarnedFromKnownUp()
    {
        const string text = "https://example.com";

        CapabilityGate
            .Unearned(text, SpamTrustTier.Untrusted, nonLatinScriptGate: false)
            .Should()
            .Equal(SpamCapability.PostLink);
        CapabilityGate
            .Unearned(text, SpamTrustTier.Newcomer, nonLatinScriptGate: false)
            .Should()
            .Equal(SpamCapability.PostLink);
        CapabilityGate
            .Unearned(text, SpamTrustTier.Known, nonLatinScriptGate: false)
            .Should()
            .BeEmpty();
        CapabilityGate
            .Unearned(text, SpamTrustTier.SemiTrusted, nonLatinScriptGate: false)
            .Should()
            .BeEmpty();
    }

    [Theory]
    [InlineData(SpamConfidence.Zero, SpamConfidence.Medium)]
    [InlineData(SpamConfidence.Low, SpamConfidence.Medium)]
    [InlineData(SpamConfidence.Medium, SpamConfidence.Medium)]
    [InlineData(SpamConfidence.High, SpamConfidence.High)] // never lowered, never beyond its own
    public void AnUnearnedCapability_RaisesConfidenceToMedium_AndNeverPastWhatTheContentSaid(
        SpamConfidence content,
        SpamConfidence expected
    ) => CapabilityGate.RaiseConfidence(content, [SpamCapability.PostLink]).Should().Be(expected);

    [Theory]
    [InlineData(SpamConfidence.Zero)]
    [InlineData(SpamConfidence.Low)]
    [InlineData(SpamConfidence.High)]
    public void NothingUnearned_LeavesTheConfidenceAlone(SpamConfidence content) =>
        CapabilityGate.RaiseConfidence(content, []).Should().Be(content);

    [Fact]
    public void TheExplanation_NamesEachCapability_AndTheTierThatEarnsIt()
    {
        string reason = CapabilityGate.Explain(
            [SpamCapability.PostLink, SpamCapability.NonLatinScript],
            CapabilityGate.FloorsFor(nonLatinScriptGate: true)
        );

        reason.Should().Contain("PostLink").And.Contain("Known");
        reason.Should().Contain("NonLatinScript").And.Contain("Regular");
    }

    [Theory]
    [InlineData("i use asp.net for that")]
    [InlineData("@QTkittE node.js is fine")]
    public void ATechName_NeedsNoLinkCapability(string text) =>
        CapabilityGate.Detect(text).Should().BeEmpty();

    [Theory]
    [InlineData("check https://gist.github.com/someone/abc")]
    [InlineData("repo is github.com/someone/thing")]
    [InlineData("go follow https://twitch.tv/somebody and https://www.youtube.com/@x")]
    public void AMessageWhoseEveryLinkIsMainstream_NeedsNoLinkCapability(string text) =>
        CapabilityGate.Detect(text).Should().BeEmpty();

    [Fact]
    public void AMainstreamLinkNextToAnUnknownOne_StillNeedsTheLinkCapability() =>
        CapabilityGate
            .Detect("https://github.com/x and https://unknown-site.xyz/buy")
            .Should()
            .Equal(SpamCapability.PostLink);

    [Fact]
    public void ALinkToAnUnknownHost_StillNeedsTheLinkCapability() =>
        CapabilityGate
            .Detect("watch this https://example.com/clip")
            .Should()
            .Equal(SpamCapability.PostLink);

    [Fact]
    public void AChannelAllowedHost_NeedsNoLinkCapability_AndOtherHostsStillDo()
    {
        string[] allowed = ["merch.example.com"];

        CapabilityGate.Detect("https://merch.example.com/shirt", allowed).Should().BeEmpty();
        CapabilityGate
            .Detect("https://other.example.com/shirt", allowed)
            .Should()
            .Equal(SpamCapability.PostLink);
    }

    [Fact]
    public void AMainstreamLink_IsNotUnearned_ForANewcomer() =>
        CapabilityGate
            .Unearned(
                "https://gist.github.com/x/y",
                SpamTrustTier.Newcomer,
                nonLatinScriptGate: false
            )
            .Should()
            .BeEmpty();
}
