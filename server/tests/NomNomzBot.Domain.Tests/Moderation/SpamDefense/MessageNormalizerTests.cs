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
/// The L0 corpus test (spam-defense.md §8). Two halves, and the second matters more than the first:
///
/// <para><b>Evasion</b> — every mutation of the motivating phrase must collapse to ONE skeleton, so a
/// single corpus entry covers all of them and every future respacing. The real message that motivated the
/// spec rendered as <c>VI EWERS ON THE STREAM</c>; the WolfwithSword list carries six hand-written entries
/// chasing mutations of two words, which is the whole argument for normalizing before matching.</para>
///
/// <para><b>False positives</b> — Japanese, Korean, Russian, Arabic, emoji and kaomoji are what real
/// viewers type. If normalization mangled them into something that collides with a corpus entry, the
/// system would punish ordinary chat, which is the exact failure SD0 exists to prevent.</para>
/// </summary>
public class MessageNormalizerTests
{
    private const string Target = "viewersonthestream";

    [Theory]
    [InlineData("best viewers on the stream")] // plain, for the baseline
    [InlineData("VI EWERS ON THE STREAM")] // word splitting — the real observed message
    [InlineData("v.i.e.w.e.r.s o.n t.h.e s.t.r.e.a.m")] // punctuation injection
    [InlineData("vie̟wers on the strea̟m")] // combining diacriticals (zalgo-lite)
    [InlineData("ѵiewers оn thе ѕtream")] // Cyrillic homoglyphs
    [InlineData("ｖｉｅｗｅｒｓ ｏｎ ｔｈｅ ｓｔｒｅａｍ")] // fullwidth
    [InlineData("v13w3r5 0n th3 5tr34m")] // leetspeak
    [InlineData("viewers​on​the​stream")] // zero-width spaces
    [InlineData("VIEWERS   ON   THE   STREAM")] // whitespace runs
    public void EveryEvasion_CollapsesToTheSameSkeleton(string message)
    {
        // "best " is a prefix on one fixture only; assert the target phrase is contained rather than
        // equal, so the table can carry realistic messages instead of stripped-down strings.
        MessageNormalizer.Normalize(message).Skeleton.Should().Contain(Target);
    }

    [Fact]
    public void TheMotivatingMessage_AndItsPlainForm_ProduceIdenticalSkeletons()
    {
        // One corpus entry has to cover both, or the operator is back to writing a line per mutation.
        MessageNormalizer
            .Normalize("VI EWERS ON THE STREAM")
            .Skeleton.Should()
            .Be(MessageNormalizer.Normalize("viewers on the stream").Skeleton);
    }

    [Theory]
    [InlineData("こんにちは、配信たのしい")] // Japanese
    [InlineData("안녕하세요 방송 재미있어요")] // Korean
    [InlineData("привет стример как дела")] // Russian (wholly one script — NOT mixed)
    [InlineData("مرحبا كيف حالك")] // Arabic
    [InlineData("¯\\_(ツ)_/¯")] // kaomoji
    [InlineData("gg ez clap")] // ordinary English chat
    public void RealViewerChat_IsNeverFlaggedAsCosmeticAbuse(string message)
    {
        NormalizedMessage result = MessageNormalizer.Normalize(message);

        result
            .StrippedCosmeticAbuse.Should()
            .BeFalse("ordinary chat in any language must not trip the SD2 cosmetic-abuse signal");
        result
            .MixedScriptTokens.Should()
            .BeEmpty("a message written wholly in one script is not script-MIXING");
    }

    [Fact]
    public void CosmeticAbuse_IsRecorded_EvenThoughTheCharactersAreStripped()
    {
        // The characters are gone from the skeleton, but the FACT of them is the signal (SD2/SD7).
        NormalizedMessage zalgo = MessageNormalizer.Normalize("B̟est vie̟wers");
        NormalizedMessage zeroWidth = MessageNormalizer.Normalize("vie​wers");

        zalgo.StrippedCosmeticAbuse.Should().BeTrue();
        zeroWidth.StrippedCosmeticAbuse.Should().BeTrue();
        MessageNormalizer
            .Normalize("best viewers")
            .StrippedCosmeticAbuse.Should()
            .BeFalse("a clean message must not report abuse it does not contain");
    }

    [Fact]
    public void MixedScriptTokens_AreRecordedBeforeFolding_BecauseFoldingDestroysTheEvidence()
    {
        // `ѕtream` is Cyrillic ѕ inside a Latin word — near-zero false-positive rate, and unrecoverable
        // once folded. Recording it here is what lets L2 use it as a signal later.
        NormalizedMessage result = MessageNormalizer.Normalize("watch the ѕtream now");

        result.MixedScriptTokens.Should().ContainSingle().Which.Should().Be("ѕtream");
        result.Skeleton.Should().Contain("stream", "the fold still happens for matching");
    }

    [Fact]
    public void RunsCollapseToTwo_NotToOne_SoRealDoubledLettersSurvive()
    {
        // Collapsing to one would break ordinary words: "coffee" would become "cofe".
        MessageNormalizer.Normalize("heeeeey").Skeleton.Should().Be("heey");
        MessageNormalizer.Normalize("coffee").Skeleton.Should().Be("coffee");
    }

    [Fact]
    public void RunPadding_IsNotFullyDefeatedByL0_AndTheLimitIsPinnedHereRatherThanAssumedAway()
    {
        // Collapse-to-TWO is the spec's rule, and it has a consequence worth stating: run padding does
        // NOT reduce to the unpadded skeleton, so exact corpus matching alone misses it. This is the same
        // documented limit as `bestviewers` vs `bestviewerson` — SimHash near-duplicate matching (L2) is
        // what closes it, which is exactly why §L2 carries both exact and near-duplicate matching rather
        // than either alone. Pinned as a test so nobody "fixes" collapse-to-one and breaks real words.
        string padded = MessageNormalizer.Normalize("viewersssss on the streammm").Skeleton;
        string plain = MessageNormalizer.Normalize("viewers on the stream").Skeleton;

        padded.Should().NotBe(plain);
        padded.Should().Be("viewerssonthestreamm", "runs collapse to two, they do not vanish");
    }

    [Fact]
    public void PunctuationAndEmojiOnly_ProducesAnEmptySkeleton_RatherThanAFalseMatch()
    {
        // A skeleton of "" must be recognisable as nothing-to-match, not silently compared to the corpus.
        NormalizedMessage result = MessageNormalizer.Normalize("!!! 🎉🎉🎉 ???");

        result.IsEmpty.Should().BeTrue();
        result.Skeleton.Should().BeEmpty();
    }

    [Fact]
    public void TheOriginalIsCarriedThrough_Unmutated()
    {
        // The normalizer decides; it never changes what chat displays.
        const string sent = "VI EWERS ON THE STREAM 🎉";

        MessageNormalizer.Normalize(sent).Original.Should().Be(sent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyInput_IsHandled_WithoutThrowing(string? message)
    {
        NormalizedMessage result = MessageNormalizer.Normalize(message);

        result.IsEmpty.Should().BeTrue();
        result.StrippedCosmeticAbuse.Should().BeFalse();
    }

    [Fact]
    public void KnownSpamPhrases_FoldOntoOneSkeletonEach_AcrossTheirMutations()
    {
        // The WolfwithSword list carries `bigfollows`, `igfollows`, `ͧ(ͧbigfollows` and `B͟est Viewers` as
        // separate entries because the tool consuming it has no normalizer. Under L0 they collapse.
        string[] bigFollows =
        [
            "bigfollows",
            "B1gFoll0ws",
            "ｂｉｇｆｏｌｌｏｗｓ",
            "b­i­g­f­o­l­l­o­w­s",
        ];

        IEnumerable<string> skeletons = bigFollows.Select(m =>
            MessageNormalizer.Normalize(m).Skeleton
        );

        skeletons
            .Distinct()
            .Should()
            .ContainSingle("four hand-written blocklist entries become one corpus skeleton");
    }
}
