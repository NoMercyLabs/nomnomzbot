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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the prune-safe decision at the heart of the Plane-A standing reconcile
/// (<see cref="CommunityStandingService.StandingToApply"/> / <see cref="CommunityStandingService.IsReconcilable"/>):
/// Twitch is authoritative for the Helix-seeded Subscriber/Vip rows it OWNS, but a partial read never lowers a
/// standing (a signal it could not read might still hold a higher one) and a non-owned Artist / Moderator / manual
/// standing is never clobbered. This is the logic that fixes "a lapsed sub still reads as Subscriber" without ever
/// wrongly downgrading a live one.
/// </summary>
public sealed class CommunityStandingReconcileTests
{
    // ── IsReconcilable: the reconcile owns ONLY Helix-seeded Subscriber/Vip rows ──────────────────────────────

    [Theory]
    [InlineData(StandingSource.HelixSeed, CommunityStanding.Subscriber, true)]
    [InlineData(StandingSource.HelixSeed, CommunityStanding.Vip, true)]
    [InlineData(StandingSource.HelixSeed, CommunityStanding.Artist, false)] // manual/custom rung — not ours
    [InlineData(StandingSource.HelixSeed, CommunityStanding.Moderator, false)] // Plane-B mirror — not ours
    [InlineData(StandingSource.HelixSeed, CommunityStanding.Everyone, false)] // dormant — nothing to prune
    [InlineData(StandingSource.EventSubBadge, CommunityStanding.Subscriber, false)] // a different source owns it
    [InlineData(StandingSource.ChatTags, CommunityStanding.Vip, false)]
    public void IsReconcilable_owns_only_helix_seeded_sub_and_vip(
        StandingSource source,
        CommunityStanding standing,
        bool expected
    ) => CommunityStandingService.IsReconcilable(source, standing).Should().Be(expected);

    // ── StandingToApply: create / raise / authoritative-downgrade / never-clobber ─────────────────────────────

    [Fact]
    public void No_existing_row_creates_at_the_twitch_standing()
    {
        CommunityStandingService
            .StandingToApply(null, null, CommunityStanding.Subscriber, fullyAuthoritative: false)
            .Should()
            .Be(
                CommunityStanding.Subscriber,
                "a viewer Twitch reports as a sub with no row yet is seeded"
            );
    }

    [Fact]
    public void Owned_subscriber_promoted_to_vip_even_on_a_partial_read()
    {
        // A raise is always safe — it never over-drops. VIP outranks Subscriber, so a sub who is now a VIP goes up
        // regardless of authoritativeness.
        CommunityStandingService
            .StandingToApply(
                CommunityStanding.Subscriber,
                StandingSource.HelixSeed,
                CommunityStanding.Vip,
                fullyAuthoritative: false
            )
            .Should()
            .Be(CommunityStanding.Vip);
    }

    [Fact]
    public void Owned_vip_is_not_lowered_to_subscriber_on_a_partial_read()
    {
        // THE prune-safe guarantee: a lower reading on a partial run is NOT trusted — the VIP read may have failed
        // while the sub read succeeded, so the still-VIP viewer must keep VIP. null = leave the row untouched.
        CommunityStandingService
            .StandingToApply(
                CommunityStanding.Vip,
                StandingSource.HelixSeed,
                CommunityStanding.Subscriber,
                fullyAuthoritative: false
            )
            .Should()
            .BeNull("a partial read must never downgrade an owned standing");
    }

    [Fact]
    public void Owned_vip_is_lowered_to_subscriber_on_a_fully_authoritative_read()
    {
        // Both signals complete = the full picture: a viewer read as a sub but not a VIP genuinely lost VIP.
        CommunityStandingService
            .StandingToApply(
                CommunityStanding.Vip,
                StandingSource.HelixSeed,
                CommunityStanding.Subscriber,
                fullyAuthoritative: true
            )
            .Should()
            .Be(CommunityStanding.Subscriber);
    }

    [Theory]
    [InlineData(CommunityStanding.Artist, CommunityStanding.Vip)] // Artist(6) > Vip(4)
    [InlineData(CommunityStanding.Moderator, CommunityStanding.Subscriber)] // Moderator(10) > Subscriber(2)
    public void A_higher_non_owned_standing_is_never_clobbered(
        CommunityStanding current,
        CommunityStanding desired
    )
    {
        // Even on a fully-authoritative read, a standing the reconcile does not own and that outranks the Twitch
        // signal is left exactly as it is — the sub/VIP fact is subsumed by the higher rung.
        CommunityStandingService
            .StandingToApply(current, StandingSource.HelixSeed, desired, fullyAuthoritative: true)
            .Should()
            .BeNull("a higher, non-owned standing is outside the reconcile's authority");
    }

    [Fact]
    public void A_dormant_everyone_row_is_raised_when_the_viewer_subscribes_again()
    {
        // A prior lapse left an Everyone/HelixSeed row; a re-sub raises it (Everyone(0) < Subscriber(2)).
        CommunityStandingService
            .StandingToApply(
                CommunityStanding.Everyone,
                StandingSource.HelixSeed,
                CommunityStanding.Subscriber,
                fullyAuthoritative: false
            )
            .Should()
            .Be(CommunityStanding.Subscriber);
    }

    [Fact]
    public void A_badge_sourced_row_is_raised_but_never_lowered_by_the_helix_reconcile()
    {
        // A standing owned by a different source (EventSubBadge) is only taken over to RAISE it, never to lower it.
        CommunityStandingService
            .StandingToApply(
                CommunityStanding.Subscriber,
                StandingSource.EventSubBadge,
                CommunityStanding.Vip,
                fullyAuthoritative: true
            )
            .Should()
            .Be(CommunityStanding.Vip, "a strict raise takes over a foreign-source row");

        CommunityStandingService
            .StandingToApply(
                CommunityStanding.Subscriber,
                StandingSource.EventSubBadge,
                CommunityStanding.Subscriber,
                fullyAuthoritative: true
            )
            .Should()
            .BeNull("an equal, foreign-source standing is left to its own source");
    }
}
