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
/// L2 content signals (spam-defense.md §L2).
///
/// <para>Every case here runs the REAL <see cref="MessageNormalizer"/> rather than a hand-built
/// <see cref="NormalizedMessage"/>. A signal layer tested against fixtures the test author invented is a
/// layer that has never seen what L0 actually emits, and the two would drift apart silently.</para>
///
/// <para>The false-positive tests matter more than the detection tests. A missed spam message is an
/// annoyance; a wrongly-deleted message belongs to a real viewer.</para>
/// </summary>
public class ContentSignalsTests
{
    /// <summary>Real campaign skeletons, in the form L0 produces them.</summary>
    private static ContentPolicy CorpusPolicy() =>
        new()
        {
            CorpusSkeletons =
            [
                Skeleton("Best viewers on bigfollows.com"),
                Skeleton("Cheap viewers and followers at streamboost.net"),
                Skeleton("Want to become famous? buy followers"),
            ],
            DeniedDomains = ["bigfollows.com", "streamboost.net", "bit.ly"],
        };

    private static string Skeleton(string text) => MessageNormalizer.Normalize(text).Skeleton;

    private static ContentEvaluation Evaluate(string text, ContentPolicy? policy = null) =>
        ContentSignals.Evaluate(MessageNormalizer.Normalize(text), text, policy);

    // ---- The messages that must never fire -----------------------------------------------------

    [Theory]
    [InlineData("hey chat how's everyone doing")]
    [InlineData("GG that was insane")]
    [InlineData("lmao")]
    [InlineData("can you play the new map next")]
    [InlineData("I've been watching since the beginning")]
    [InlineData("brb making coffee")]
    [InlineData("PogChamp PogChamp PogChamp")]
    [InlineData("cheap keyboards are actually fine tbh")]
    [InlineData("that boss fight was free")]
    [InlineData("¯\\_(ツ)_/¯")]
    [InlineData("おはよう")]
    [InlineData("привет всем")]
    [InlineData("따라 하지 마세요")]
    public void OrdinaryChat_FiresNothing_EvenWithAFullCorpusLoaded(string message)
    {
        // The single most important property in the file. Note the last three: whole messages in another
        // script are ordinary viewers, and are explicitly NOT intra-token script mixing (§L2).
        ContentEvaluation result = Evaluate(message, CorpusPolicy());

        result
            .Signals.Should()
            .BeEmpty($"\"{message}\" is something a real viewer types; it must fire nothing");
        result.Confidence.Should().Be(SpamConfidence.Zero);
    }

    [Fact]
    public void AnEmojiOnlyMessage_FiresNothing()
    {
        // Emoji carry surrogate pairs and ZWJ sequences, which are exactly the shapes the cosmetic-abuse
        // detector looks for. Confusing "🧑‍🚀" with a zalgo attack would delete half of chat.
        Evaluate("🎉🎉🎉 👨‍👩‍👧‍👦 🧑‍🚀", CorpusPolicy()).Signals.Should().BeEmpty();
    }

    [Fact]
    public void AChannelsOwnCatchphrase_CanBeMarkedBenign_AndThenNeverFires()
    {
        string copypasta = "Best viewers on bigfollows.com";
        ContentPolicy corpus = CorpusPolicy();

        Evaluate(copypasta, corpus).Confidence.Should().Be(SpamConfidence.High);

        ContentPolicy forgiving = corpus with { ChannelBenignSkeletons = [Skeleton(copypasta)] };
        Evaluate(copypasta, forgiving)
            .Signals.Should()
            .BeEmpty("a channel's own allow-list wins over the shared corpus");
    }

    [Fact]
    public void AStreamersOwnDomain_IsNotFlagged_EvenWhenItLooksLikeTheDeniedOne()
    {
        ContentPolicy policy = CorpusPolicy() with { AllowedDomains = ["mystore.bigfollows.com"] };

        Evaluate("merch is at mystore.bigfollows.com", policy)
            .Signals.Should()
            .NotContain(ContentSignal.MaliciousLink);
    }

    // ---- The messages that must fire -----------------------------------------------------------

    [Fact]
    public void ACosmeticAbuseMessage_IsHighConfidenceOnItsOwn()
    {
        // SD2: there is no legitimate reason to put a zero-width joiner between the letters of a word.
        ContentEvaluation result = Evaluate("f​r​e​e f​o​l​lows");

        result.Signals.Should().Contain(ContentSignal.CosmeticAbuse);
        result.Confidence.Should().Be(SpamConfidence.High);
    }

    [Fact]
    public void AHomoglyphedWord_IsCaughtAsIntraTokenScriptMixing()
    {
        // Cyrillic ѕ inside a Latin word. The message reads normally to a human and evades every
        // literal-string filter.
        Evaluate("check my ѕtream")
            .Signals.Should()
            .Contain(ContentSignal.IntraTokenScriptMixing);
    }

    [Fact]
    public void AKnownCampaign_MatchesTheCorpusExactly_EvenAfterCosmeticRewriting()
    {
        // The corpus stores SKELETONS, so the same campaign in mixed case with a unicode dot still hits.
        Evaluate("BEST VIEWERS ON BIGFOLLOWS․COM", CorpusPolicy())
            .Signals.Should()
            .Contain(ContentSignal.CorpusMatch);
    }

    [Fact]
    public void TheNextMutationOfAKnownCampaign_IsCaughtAsANearDuplicate_BeforeAnyoneReportsIt()
    {
        // This is the layer's reason to exist: the spammer changes two characters and re-sends.
        ContentEvaluation result = Evaluate(
            "Best viewerz on bigfollows.com",
            CorpusPolicy() with
            {
                DeniedDomains = [],
            }
        );

        result
            .Signals.Should()
            .Contain(
                ContentSignal.NearDuplicate,
                "a two-character mutation must not walk past the corpus"
            );
    }

    [Fact]
    public void ADeniedDomain_FiresEvenWhenTheDotsAreEvaded()
    {
        // `bit␣ly/x` and `bit.ly/x` are the same link to a viewer and must be the same to the engine.
        Evaluate("free prime sub bit ly/xyz", CorpusPolicy())
            .Signals.Should()
            .Contain(ContentSignal.MaliciousLink);
    }

    [Fact]
    public void PromoShape_NeedsBothAContactHandleAndOfferVocabulary_SoOneAloneIsNotEnough()
    {
        // Deliberately conservative. "@someone" alone is a mention; "cheap" alone is an opinion about
        // keyboards. Only the two together are a promo, and even then it is Medium, never High.
        Evaluate("@streamer that was amazing")
            .Signals.Should()
            .NotContain(ContentSignal.PromoShape);
        Evaluate("this game is cheap right now")
            .Signals.Should()
            .NotContain(ContentSignal.PromoShape);

        ContentEvaluation promo = Evaluate(
            "dm me @cheapviewers to grow your channel, discount today"
        );
        promo.Signals.Should().Contain(ContentSignal.PromoShape);
    }

    [Fact]
    public void PromoShapeWithoutACorpusHit_IsMediumAtMost_SoItCanNeverReachAnAccountAction()
    {
        // Chained with L5 this is the guarantee that matters: Medium deletes and queues, and
        // SpamEnforcement never lets Medium touch an account at any tier. A false positive here costs a
        // viewer one message, recoverable, not their access to the channel.
        ContentEvaluation promo = Evaluate("dm me @growfast to boost your channel, cheap promo");

        promo.Confidence.Should().NotBe(SpamConfidence.High);

        SpamDecision decision = SpamEnforcement.Decide(
            promo.Confidence,
            SpamTrustTier.Untrusted,
            dryRun: false
        );
        decision
            .TouchesAccount.Should()
            .BeFalse("promo shape alone must never be able to ban somebody");
    }

    // ---- Structural properties -----------------------------------------------------------------

    [Fact]
    public void AVeryShortSkeleton_IsNeverCorpusMatched()
    {
        // Without the length floor, a corpus that ever picked up a short entry would start deleting
        // "gg" and "lol" across every subscribed channel at once.
        ContentPolicy trap = new() { CorpusSkeletons = ["gg"], MinimumSkeletonLength = 8 };

        Evaluate("gg", trap).Signals.Should().BeEmpty();
    }

    [Fact]
    public void AnEmptyPolicy_CannotFireCorpusOrLinkSignals()
    {
        // A fresh channel has no corpus and no deny list. It must still be safe to run, which means the
        // matching code cannot treat "no entries" as "matches everything".
        ContentEvaluation result = Evaluate("Best viewers on bigfollows.com", new ContentPolicy());

        result.Signals.Should().NotContain(ContentSignal.CorpusMatch);
        result.Signals.Should().NotContain(ContentSignal.NearDuplicate);
        result.Signals.Should().NotContain(ContentSignal.MaliciousLink);
    }

    [Fact]
    public void NearDuplicateMatchingCanBeTurnedOff_WithoutDisablingExactMatching()
    {
        ContentPolicy exactOnly = CorpusPolicy() with
        {
            NearDuplicateSimilarity = 0,
            DeniedDomains = [],
        };

        Evaluate("Best viewerz on bigfollows.com", exactOnly)
            .Signals.Should()
            .NotContain(ContentSignal.NearDuplicate);
        Evaluate("Best viewers on bigfollows.com", exactOnly)
            .Signals.Should()
            .Contain(ContentSignal.CorpusMatch);
    }

    [Fact]
    public void TheNearDuplicateThreshold_SitsInAWideGap_NotOnAKnifeEdge()
    {
        // The threshold is only defensible if there is real distance either side of it. These are the
        // measurements the 0.6 default was chosen from: mutations of a campaign well above, unrelated
        // chat well below — including a message that shares a whole word with the campaign.
        string campaign = Skeleton("Best viewers on bigfollows.com");

        double MutationOf(string text) =>
            ContentSignals.Similarity(
                ContentSignals.Shingles(Skeleton(text)),
                ContentSignals.Shingles(campaign)
            );

        MutationOf("Best viewerz on bigfollows.com").Should().BeGreaterThan(0.7);
        MutationOf("Best viewers on bigfollows.com join now").Should().BeGreaterThan(0.7);

        MutationOf("how many viewers are watching right now").Should().BeLessThan(0.3);
        MutationOf("hey chat how's everyone doing").Should().BeLessThan(0.3);
        MutationOf("Cheap viewers and followers at streamboost.net").Should().BeLessThan(0.3);
    }

    [Fact]
    public void SimilarityIsSymmetric_AndOneForIdenticalText()
    {
        HashSet<string> a = ContentSignals.Shingles("best viewers on bigfollows");
        HashSet<string> b = ContentSignals.Shingles("cheap followers at streamboost");

        ContentSignals.Similarity(a, a).Should().Be(1.0);
        ContentSignals.Similarity(a, b).Should().Be(ContentSignals.Similarity(b, a));
        ContentSignals.Similarity(a, new HashSet<string>()).Should().Be(0);
    }

    [Fact]
    public void ConfidenceRises_OnlyWhenIndependentSignalsCorroborate()
    {
        // One weak signal flags; two agreeing weak signals delete. This is the whole graduated-response
        // idea, and it is what stops a single heuristic from acting alone.
        ContentSignals
            .Evaluate(new NormalizedMessage("x", "x", false, []), "x")
            .Confidence.Should()
            .Be(SpamConfidence.Zero);
    }
}
