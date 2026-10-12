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
/// The 2026-10-12 owner corrections (spam-defense.md): ordinary chat the content signals marked. Every
/// case runs the real normalizer and asserts the signal list and the confidence.
/// </summary>
public class ContentFalseFlagTests
{
    private static ContentEvaluation Evaluate(string text) =>
        ContentSignals.Evaluate(MessageNormalizer.Normalize(text), text);

    private static void ShouldBeClean(string text)
    {
        ContentEvaluation result = Evaluate(text);
        result.Signals.Should().BeEmpty();
        result.Confidence.Should().Be(SpamConfidence.Zero);
    }

    [Theory]
    [InlineData("@QTkittE i mean AGI ha")]
    [InlineData("@QTkittE @someone that was fun, cheap trick though")]
    public void AReplyThatStartsWithAName_IsNotAPromoContactHandle(string text) =>
        ShouldBeClean(text);

    [Theory]
    [InlineData("dm @bigboost for cheap followers")]
    [InlineData("@QTkittE dm @bigboost for cheap followers")]
    public void APromoWithAHandle_IsStillPromoShape(string text) =>
        Evaluate(text).Signals.Should().Equal(ContentSignal.PromoShape);

    [Theory]
    [InlineData("Straße")]
    [InlineData("es ist heiß heute")]
    [InlineData("Fußball ist toll")]
    public void AGermanEszettInALatinWord_IsNotScriptMixing(string text)
    {
        MessageNormalizer.Normalize(text).MixedScriptTokens.Should().BeEmpty();
        ShouldBeClean(text);
    }

    [Fact]
    public void ACyrillicLetterInsideALatinWord_IsStillScriptMixing() =>
        Evaluate("ѕtream").Signals.Should().Equal(ContentSignal.IntraTokenScriptMixing);

    [Theory]
    [InlineData("oh adele 😫❤️")]
    [InlineData("go ➡️ now")]
    [InlineData("flight ✈️ booked")]
    [InlineData("call me ☎️")]
    [InlineData("hot spring ♨️")]
    [InlineData("wave 👋🏽 hi")]
    [InlineData("family 👨‍👩‍👧")]
    [InlineData("rainbow 🏳️‍🌈")]
    [InlineData("england 🏴󠁧󠁢󠁥󠁮󠁧󠁿")]
    [InlineData("press 1️⃣")]
    public void EveryPartOfAnEmoji_IsNotCosmeticAbuse(string text)
    {
        MessageNormalizer.Normalize(text).StrippedCosmeticAbuse.Should().BeFalse();
        ShouldBeClean(text);
    }

    [Theory]
    [InlineData("good game͏")]
    [InlineData("good game ͏")]
    [InlineData("good game 󠀀")]
    [InlineData("good game 󠀂")]
    [InlineData("good game 󠀀͏")]
    public void TheInvisibleRepeatMarkerAtTheEnd_IsNotCosmeticAbuse(string text)
    {
        MessageNormalizer.Normalize(text).StrippedCosmeticAbuse.Should().BeFalse();
        ShouldBeClean(text);
    }

    [Theory]
    [InlineData("😫😫😫😫😫😫")]
    [InlineData("❤️❤️❤️❤️❤️❤️")]
    public void AMessageOfOnlyEmoji_HasConfidenceZero(string text) => ShouldBeClean(text);

    [Theory]
    [InlineData("f​ree")]
    [InlineData("fr‮ee")]
    [InlineData("f͏ree")]
    [InlineData("à́̂̃̄̅̆b")]
    [InlineData("hello​")]
    public void RealCosmeticAbuse_StillFires(string text)
    {
        ContentEvaluation result = Evaluate(text);
        result.Signals.Should().Contain(ContentSignal.CosmeticAbuse);
        result.Confidence.Should().Be(SpamConfidence.High);
    }
}
