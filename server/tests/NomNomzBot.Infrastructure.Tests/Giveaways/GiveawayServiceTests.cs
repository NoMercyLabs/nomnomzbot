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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Giveaways.Dtos;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Domain.Giveaways.Entities;
using NomNomzBot.Domain.Giveaways.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Giveaways;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Giveaways;

/// <summary>
/// Proves the giveaway campaign mechanics (giveaways.md §8): the single-active guard; entry dedupe, the
/// spend_giveaway cost debit (with the honest INSUFFICIENT_FUNDS propagation and NO orphan entry row),
/// eligibility rejections with reasons, and sub-luck ticket weighting; the draw appending exactly
/// WinnerCount distinct winner rows — never the broadcaster — publishing GiveawayDrawnEvent with the
/// right ids, running fulfillment per winner, and flagging code-pool exhaustion loudly; the re-roll
/// marking + replacing (never rewriting) while excluding every prior winner.
/// </summary>
public sealed class GiveawayServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("0193a000-0000-7000-8000-0000000000a1");
    private static readonly Guid Owner = Guid.Parse("0193a000-0000-7000-8000-0000000000a9");

    private sealed record Harness(
        GiveawayService Service,
        AuthDbContext Db,
        ICurrencyAccountService Accounts,
        IGiveawayFulfillment Fulfillment,
        IEventBus Bus
    );

    private static Harness Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                OwnerUserId = Owner,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tw-1",
                TwitchChannelId = "tw-1",
                Name = "streamer",
                NameNormalized = "streamer",
                IsOnboarded = true,
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
                BillingTierKey = "free",
            }
        );
        db.SaveChanges();

        ICurrencyAccountService accounts = Substitute.For<ICurrencyAccountService>();
        accounts
            .PostLedgerEntryAsync(
                Arg.Any<Guid>(),
                Arg.Any<PostLedgerEntryCommand>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => Result.Success(LedgerEntry(42)));

        IGiveawayFulfillment fulfillment = Substitute.For<IGiveawayFulfillment>();
        IEventBus bus = Substitute.For<IEventBus>();
        IUnitOfWork unitOfWork = Substitute.For<IUnitOfWork>();

        GiveawayService service = new(
            db,
            unitOfWork,
            bus,
            accounts,
            fulfillment,
            TimeProvider.System,
            NullLogger<GiveawayService>.Instance
        );
        return new Harness(service, db, accounts, fulfillment, bus);
    }

    private static CurrencyLedgerEntryDto LedgerEntry(long id) =>
        new(
            Id: id,
            TenantPosition: id,
            AccountId: Guid.NewGuid(),
            ViewerUserId: Guid.NewGuid(),
            Amount: -25,
            BalanceAfter: 975,
            EntryType: nameof(CurrencyEntryType.SpendGiveaway),
            SourceType: nameof(CurrencyLedgerSourceType.Giveaway),
            SourceId: null,
            RelatedEntryId: null,
            EventId: null,
            Reason: null,
            ActorUserId: null,
            CreatedAt: DateTime.UtcNow
        );

    private static Guid SeedViewer(
        AuthDbContext db,
        string twitchId,
        CommunityStanding? standing = null,
        string? subTier = null
    )
    {
        User viewer = new()
        {
            Id = Guid.CreateVersion7(),
            Username = $"viewer-{twitchId}",
            UsernameNormalized = $"viewer-{twitchId}",
            DisplayName = $"Viewer {twitchId}",
            TwitchUserId = twitchId,
        };
        db.Users.Add(viewer);
        if (standing is { } s)
            db.ChannelCommunityStandings.Add(
                new ChannelCommunityStanding
                {
                    BroadcasterId = Tenant,
                    UserId = viewer.Id,
                    Standing = s,
                    LevelValue = s switch
                    {
                        CommunityStanding.Subscriber => 2,
                        CommunityStanding.Vip => 4,
                        CommunityStanding.Artist => 6,
                        CommunityStanding.Moderator => 10,
                        _ => 0,
                    },
                    SubTier = subTier,
                }
            );
        db.SaveChanges();
        return viewer.Id;
    }

    private static async Task<Guid> SeedOpenGiveawayAsync(
        Harness harness,
        UpsertGiveawayRequest? request = null
    )
    {
        Result<GiveawayDto> created = await harness.Service.CreateAsync(
            Tenant,
            request
                ?? new UpsertGiveawayRequest(
                    "Test Drop",
                    GiveawayEntryMode.Keyword,
                    Keyword: "!win"
                ),
            CancellationToken.None
        );
        created.IsSuccess.Should().BeTrue(created.ErrorMessage);
        Result<GiveawayDto> opened = await harness.Service.OpenAsync(
            Tenant,
            created.Value.Id,
            CancellationToken.None
        );
        opened.IsSuccess.Should().BeTrue(opened.ErrorMessage);
        return created.Value.Id;
    }

    [Fact]
    public async Task Only_one_giveaway_can_be_active_per_channel()
    {
        Harness harness = Build();
        await SeedOpenGiveawayAsync(harness);

        Result<GiveawayDto> second = await harness.Service.CreateAsync(
            Tenant,
            new UpsertGiveawayRequest("Second", GiveawayEntryMode.Keyword, Keyword: "!two"),
            CancellationToken.None
        );
        Result<GiveawayDto> opened = await harness.Service.OpenAsync(
            Tenant,
            second.Value.Id,
            CancellationToken.None
        );

        opened.IsFailure.Should().BeTrue();
        opened.ErrorCode.Should().Be("GIVEAWAY_ALREADY_ACTIVE");
    }

    [Fact]
    public async Task An_unverifiable_follower_requirement_is_rejected_at_configuration()
    {
        Harness harness = Build();

        Result<GiveawayDto> created = await harness.Service.CreateAsync(
            Tenant,
            new UpsertGiveawayRequest(
                "Bad Config",
                GiveawayEntryMode.Keyword,
                Keyword: "!win",
                EligibilityJson: """{"require_follower":true}"""
            ),
            CancellationToken.None
        );

        created.IsFailure.Should().BeTrue("an unenforceable filter must never be silently ignored");
        created.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task Entry_is_deduped_and_a_paid_entry_links_its_ledger_debit()
    {
        Harness harness = Build();
        Guid giveawayId = await SeedOpenGiveawayAsync(
            harness,
            new UpsertGiveawayRequest(
                "Paid Drop",
                GiveawayEntryMode.Keyword,
                Keyword: "!win",
                EntryCost: 25
            )
        );
        Guid viewer = SeedViewer(harness.Db, "111");

        Result<GiveawayEntryDto> first = await harness.Service.EnterAsync(
            Tenant,
            giveawayId,
            viewer,
            CancellationToken.None
        );
        Result<GiveawayEntryDto> second = await harness.Service.EnterAsync(
            Tenant,
            giveawayId,
            viewer,
            CancellationToken.None
        );

        first.IsSuccess.Should().BeTrue(first.ErrorMessage);
        second.IsFailure.Should().BeTrue();
        second.ErrorCode.Should().Be("ALREADY_ENTERED");

        GiveawayEntry entry = harness.Db.GiveawayEntries.Single();
        entry.EntryCostLedgerEntryId.Should().Be(42, "the debit's ledger id is the audit link");
        await harness
            .Accounts.Received(1)
            .PostLedgerEntryAsync(
                Tenant,
                Arg.Is<PostLedgerEntryCommand>(c =>
                    c.Amount == -25
                    && c.EntryType == nameof(CurrencyEntryType.SpendGiveaway)
                    && c.ViewerUserId == viewer
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_broke_viewer_is_rejected_and_no_entry_row_is_written()
    {
        Harness harness = Build();
        Guid giveawayId = await SeedOpenGiveawayAsync(
            harness,
            new UpsertGiveawayRequest(
                "Paid Drop",
                GiveawayEntryMode.Keyword,
                Keyword: "!win",
                EntryCost: 25
            )
        );
        Guid viewer = SeedViewer(harness.Db, "111");
        harness
            .Accounts.PostLedgerEntryAsync(
                Arg.Any<Guid>(),
                Arg.Any<PostLedgerEntryCommand>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<CurrencyLedgerEntryDto>("broke", "INSUFFICIENT_FUNDS"));

        Result<GiveawayEntryDto> entered = await harness.Service.EnterAsync(
            Tenant,
            giveawayId,
            viewer,
            CancellationToken.None
        );

        entered.IsFailure.Should().BeTrue();
        entered.ErrorCode.Should().Be("INSUFFICIENT_FUNDS");
        harness.Db.GiveawayEntries.Any().Should().BeFalse("a failed debit must not mint an entry");
    }

    [Fact]
    public async Task Eligibility_rejects_a_non_sub_when_subs_only()
    {
        Harness harness = Build();
        Guid giveawayId = await SeedOpenGiveawayAsync(
            harness,
            new UpsertGiveawayRequest(
                "Subs Only",
                GiveawayEntryMode.Keyword,
                Keyword: "!win",
                EligibilityJson: """{"require_sub":true}"""
            )
        );
        Guid pleb = SeedViewer(harness.Db, "111");
        Guid sub = SeedViewer(harness.Db, "222", CommunityStanding.Subscriber, subTier: "1000");

        Result<GiveawayEntryDto> rejected = await harness.Service.EnterAsync(
            Tenant,
            giveawayId,
            pleb,
            CancellationToken.None
        );
        Result<GiveawayEntryDto> accepted = await harness.Service.EnterAsync(
            Tenant,
            giveawayId,
            sub,
            CancellationToken.None
        );

        rejected.IsFailure.Should().BeTrue();
        rejected.ErrorCode.Should().Be("NOT_ELIGIBLE");
        accepted.IsSuccess.Should().BeTrue(accepted.ErrorMessage);
    }

    [Fact]
    public async Task Sub_luck_weighting_assigns_the_configured_tickets()
    {
        Harness harness = Build();
        Guid giveawayId = await SeedOpenGiveawayAsync(
            harness,
            new UpsertGiveawayRequest(
                "Weighted",
                GiveawayEntryMode.Keyword,
                Keyword: "!win",
                WeightingJson: """{"sub_t1":2,"sub_t3":4,"vip":2}"""
            )
        );
        Guid pleb = SeedViewer(harness.Db, "111");
        Guid t3 = SeedViewer(harness.Db, "333", CommunityStanding.Subscriber, subTier: "3000");

        Result<GiveawayEntryDto> plebEntry = await harness.Service.EnterAsync(
            Tenant,
            giveawayId,
            pleb,
            CancellationToken.None
        );
        Result<GiveawayEntryDto> t3Entry = await harness.Service.EnterAsync(
            Tenant,
            giveawayId,
            t3,
            CancellationToken.None
        );

        plebEntry.Value.TicketCount.Should().Be(1, "unweighted viewers hold one ticket");
        t3Entry.Value.TicketCount.Should().Be(4, "a T3 sub gets the configured multiple");
    }

    [Fact]
    public async Task Draw_appends_distinct_winners_never_the_broadcaster_and_publishes_the_event()
    {
        Harness harness = Build();
        Guid giveawayId = await SeedOpenGiveawayAsync(
            harness,
            new UpsertGiveawayRequest(
                "Two Winners",
                GiveawayEntryMode.Keyword,
                Keyword: "!win",
                WinnerCount: 2
            )
        );

        // The OWNER enters too — the draw must never pick them (D4).
        User ownerUser = new()
        {
            Id = Owner,
            Username = "streamer",
            UsernameNormalized = "streamer",
            DisplayName = "Streamer",
            TwitchUserId = "tw-1",
        };
        harness.Db.Users.Add(ownerUser);
        harness.Db.SaveChanges();
        await harness.Service.EnterAsync(Tenant, giveawayId, Owner, CancellationToken.None);
        Guid v1 = SeedViewer(harness.Db, "111");
        Guid v2 = SeedViewer(harness.Db, "222");
        Guid v3 = SeedViewer(harness.Db, "333");
        foreach (Guid viewer in (Guid[])[v1, v2, v3])
            (await harness.Service.EnterAsync(Tenant, giveawayId, viewer, CancellationToken.None))
                .IsSuccess.Should()
                .BeTrue();

        Result<IReadOnlyList<GiveawayWinnerDto>> drawn = await harness.Service.DrawAsync(
            Tenant,
            giveawayId,
            CancellationToken.None
        );

        drawn.IsSuccess.Should().BeTrue(drawn.ErrorMessage);
        drawn.Value.Should().HaveCount(2);
        drawn.Value.Select(w => w.ViewerUserId).Should().OnlyHaveUniqueItems();
        drawn.Value.Select(w => w.ViewerUserId).Should().NotContain(Owner);

        harness.Db.GiveawayWinners.Count().Should().Be(2);
        harness.Db.Giveaways.Single().Status.Should().Be(GiveawayStatus.Drawn);
        await harness
            .Fulfillment.Received(2)
            .FulfillAsync(
                Arg.Any<Giveaway>(),
                Arg.Any<GiveawayWinner>(),
                Arg.Any<CancellationToken>()
            );
        await harness
            .Bus.Received(1)
            .PublishAsync(
                Arg.Is<GiveawayDrawnEvent>(e =>
                    e.GiveawayId == giveawayId && e.WinnerUserIds.Count == 2 && e.EntryCount == 4
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Code_pool_exhaustion_is_flagged_loudly_not_silently()
    {
        Harness harness = Build();
        Guid poolId = Guid.CreateVersion7();
        Guid giveawayId = await SeedOpenGiveawayAsync(
            harness,
            new UpsertGiveawayRequest(
                "Keys",
                GiveawayEntryMode.Keyword,
                Keyword: "!win",
                PrizeMode: GiveawayPrizeMode.CodePool,
                PrizeCodePoolId: poolId
            )
        );
        Guid v1 = SeedViewer(harness.Db, "111");
        await harness.Service.EnterAsync(Tenant, giveawayId, v1, CancellationToken.None);
        // The fulfillment substitute assigns NO code (pool empty) — the draw must surface it.

        Result<IReadOnlyList<GiveawayWinnerDto>> drawn = await harness.Service.DrawAsync(
            Tenant,
            giveawayId,
            CancellationToken.None
        );

        drawn.IsFailure.Should().BeTrue();
        drawn.ErrorCode.Should().Be("CODE_POOL_EXHAUSTED");
        harness
            .Db.GiveawayWinners.Count()
            .Should()
            .Be(1, "the winner still stands, flagged un-coded");
    }

    [Fact]
    public async Task Redraw_marks_the_target_and_draws_a_replacement_excluding_all_prior_winners()
    {
        Harness harness = Build();
        Guid giveawayId = await SeedOpenGiveawayAsync(harness);
        Guid v1 = SeedViewer(harness.Db, "111");
        Guid v2 = SeedViewer(harness.Db, "222");
        foreach (Guid viewer in (Guid[])[v1, v2])
            await harness.Service.EnterAsync(Tenant, giveawayId, viewer, CancellationToken.None);

        Result<IReadOnlyList<GiveawayWinnerDto>> drawn = await harness.Service.DrawAsync(
            Tenant,
            giveawayId,
            CancellationToken.None
        );
        GiveawayWinnerDto original = drawn.Value.Single();

        Result<GiveawayWinnerDto> replacement = await harness.Service.RedrawAsync(
            Tenant,
            giveawayId,
            original.Id,
            CancellationToken.None
        );

        replacement.IsSuccess.Should().BeTrue(replacement.ErrorMessage);
        replacement.Value.IsRedraw.Should().BeTrue();
        replacement
            .Value.ViewerUserId.Should()
            .NotBe(original.ViewerUserId, "a replacement can never repeat a prior winner");

        // Append-only: the original row still exists, marked redrawn — never rewritten.
        harness.Db.GiveawayWinners.Count().Should().Be(2);
        harness
            .Db.GiveawayWinners.Single(w => w.Id == original.Id)
            .Status.Should()
            .Be(GiveawayWinnerStatus.Redrawn);
        await harness
            .Fulfillment.Received(2)
            .FulfillAsync(
                Arg.Any<Giveaway>(),
                Arg.Any<GiveawayWinner>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(null, null, 1)] // unweighted
    [InlineData("""{"sub_t1":2,"sub_t3":4}""", "1000", 2)]
    [InlineData("""{"sub_t1":2,"sub_t3":4}""", "3000", 4)]
    public void Compute_tickets_matches_the_configured_weighting(
        string? weighting,
        string? subTier,
        int expected
    )
    {
        ChannelCommunityStanding? standing = subTier is null
            ? null
            : new ChannelCommunityStanding
            {
                Standing = CommunityStanding.Subscriber,
                LevelValue = 2,
                SubTier = subTier,
            };
        GiveawayService.ComputeTickets(weighting, standing).Should().Be(expected);
    }
}
