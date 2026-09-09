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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Community.Dtos;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Giveaways.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Stream.Entities;
using NomNomzBot.Domain.ViewerData.Entities;
using NomNomzBot.Infrastructure.Community;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Community;

/// <summary>
/// Proves the Community Profile page's aggregation (owner punch list 2026-09-08 §3): every group in
/// <see cref="ViewerProfileSummaryDto"/> reflects the REAL rows in its own table, folded truthfully rather
/// than a shape that merely deserializes. Analytics/permits/TTS-voice/quotes are exercised through their own
/// dedicated service interfaces (substituted here) — this test proves ViewerProfileService wires them
/// correctly and reads its OWN direct tables (identity, moderation summary, economy, consent, overrides,
/// command usage, free-form data) correctly.
/// </summary>
public sealed class ViewerProfileServiceTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();
    private static readonly Guid Subject = Guid.CreateVersion7();

    private static (
        ViewerProfileService Sut,
        ViewerProfileServiceTestDbContext Db,
        IViewerAnalyticsService Analytics,
        IPermitService Permits,
        IQuoteService Quotes,
        ITtsConfigService Tts
    ) Build()
    {
        ViewerProfileServiceTestDbContext db = ViewerProfileServiceTestDbContext.New();
        IViewerAnalyticsService analytics = Substitute.For<IViewerAnalyticsService>();
        IPermitService permits = Substitute.For<IPermitService>();
        IQuoteService quotes = Substitute.For<IQuoteService>();
        ITtsConfigService tts = Substitute.For<ITtsConfigService>();

        analytics
            .GetProfileAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Errors.NotFound<ViewerProfileDto>("Viewer profile", ""));
        analytics
            .GetStreakAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Errors.NotFound<WatchStreakDto>("Watch streak", ""));
        permits
            .ListActiveGrantsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<PermitGrantDto>>([]));
        quotes
            .ListByUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new PagedList<QuoteDto>([], 1, 10, 0)));
        tts.GetUserVoiceAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Errors.NotFound<Application.Tts.Dtos.UserTtsVoiceDto>("TTS voice assignment", "")
            );

        ViewerProfileService sut = new(db, analytics, permits, quotes, tts);
        return (sut, db, analytics, permits, quotes, tts);
    }

    private static async Task SeedSubjectAsync(
        ViewerProfileServiceTestDbContext db,
        string twitchUserId = "subject-twitch"
    )
    {
        db.Users.Add(
            new()
            {
                Id = Subject,
                TwitchUserId = twitchUserId,
                Username = "subject",
                UsernameNormalized = "subject",
                DisplayName = "Subject",
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetProfileAsync_FailsNotFound_ForAUserWithNoLocalRow()
    {
        (ViewerProfileService sut, _, _, _, _, _) = Build();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(
            Broadcaster,
            Guid.CreateVersion7()
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task GetProfileAsync_FoldsIdentityStandingAndModerationHistory_FromTheirRealTables()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        await SeedSubjectAsync(db);

        db.ChannelCommunityStandings.Add(
            new()
            {
                BroadcasterId = Broadcaster,
                UserId = Subject,
                Standing = CommunityStanding.Vip,
                LevelValue = 20,
            }
        );
        db.UserModerationHistories.Add(
            new()
            {
                BroadcasterId = Broadcaster,
                SubjectUserId = Subject,
                SubjectTwitchUserId = "subject-twitch",
                BanCount = 1,
                TimeoutCount = 2,
                WarningCount = 3,
                LastActionType = "ban",
            }
        );
        await db.SaveChangesAsync();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        ViewerProfileSummaryDto profile = result.Value;
        profile.Identity.UserId.Should().Be(Subject);
        profile.Identity.DisplayName.Should().Be("Subject");
        profile.Identity.CommunityStanding.Should().Be(CommunityStanding.Vip.ToString());
        profile.ModerationHistory.Should().NotBeNull();
        profile.ModerationHistory!.BanCount.Should().Be(1);
        profile.ModerationHistory.TimeoutCount.Should().Be(2);
        profile.ModerationHistory.WarningCount.Should().Be(3);
    }

    [Fact]
    public async Task GetProfileAsync_ModerationHistory_IsNull_ForAPersonWithNoProjectedHistoryYet()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        await SeedSubjectAsync(db);

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result
            .Value.ModerationHistory.Should()
            .BeNull(
                "a clean viewer truthfully has no moderation history, not a zeroed-out fake one"
            );
    }

    [Fact]
    public async Task GetProfileAsync_FoldsEconomy_FromTheRealWalletAndGiveawayTables_WithoutMintingAWallet()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        await SeedSubjectAsync(db);

        db.CurrencyAccounts.Add(
            new()
            {
                BroadcasterId = Broadcaster,
                ViewerUserId = Subject,
                ViewerTwitchUserId = "subject-twitch",
                Balance = 1500,
                LifetimeEarned = 2000,
                LifetimeSpent = 500,
            }
        );
        db.GiveawayEntries.Add(
            new()
            {
                BroadcasterId = Broadcaster,
                GiveawayId = Guid.CreateVersion7(),
                ViewerUserId = Subject,
                ViewerTwitchUserId = "subject-twitch",
            }
        );
        db.GiveawayWinners.Add(
            new()
            {
                BroadcasterId = Broadcaster,
                GiveawayId = Guid.CreateVersion7(),
                ViewerUserId = Subject,
                ViewerTwitchUserId = "subject-twitch",
                Status = GiveawayWinnerStatus.Claimed,
            }
        );
        db.LeaderboardOptOuts.Add(
            new()
            {
                BroadcasterId = Broadcaster,
                ViewerUserId = Subject,
                ViewerTwitchUserId = "subject-twitch",
            }
        );
        await db.SaveChangesAsync();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        ViewerEconomyDto economy = result.Value.Economy;
        economy.Wallet.Should().NotBeNull();
        economy.Wallet!.Balance.Should().Be(1500);
        economy.GiveawayEntryCount.Should().Be(1);
        economy.GiveawayWinCount.Should().Be(1);
        economy.LeaderboardOptedOut.Should().BeTrue();
    }

    [Fact]
    public async Task GetProfileAsync_Wallet_IsNull_ForAViewerWhoNeverEarnedCurrency()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        await SeedSubjectAsync(db);

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result
            .Value.Economy.Wallet.Should()
            .BeNull(
                "viewing a profile must never lazily mint a wallet for someone who never earned one"
            );
    }

    [Fact]
    public async Task GetProfileAsync_FoldsAgeConsent_FromTheRealConsentTable()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        await SeedSubjectAsync(db);

        db.ViewerAgeConsents.Add(
            new()
            {
                BroadcasterId = Broadcaster,
                ViewerUserId = Subject,
                ViewerTwitchUserId = "subject-twitch",
                ConsentRecordId = Guid.CreateVersion7(),
                Granted = true,
                ConfirmedAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                ConfirmationMethod = "chat_command",
            }
        );
        await db.SaveChangesAsync();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.Value.Permits.AgeConsentGranted.Should().BeTrue();
        result
            .Value.Permits.AgeConsentConfirmedUtc.Should()
            .Be(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetProfileAsync_FoldsShoutoutAndRaidOverrides_ByKind_FromTheirOwnRows()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        await SeedSubjectAsync(db);

        db.ShoutoutOverrides.Add(
            new()
            {
                BroadcasterId = Broadcaster,
                TargetTwitchUserId = "subject-twitch",
                TargetDisplayName = "Subject",
                MessageTemplate = "Go check out Subject!",
                Kind = ShoutoutOverrideKinds.Shoutout,
            }
        );
        db.ShoutoutOverrides.Add(
            new()
            {
                BroadcasterId = Broadcaster,
                TargetTwitchUserId = "subject-twitch",
                TargetDisplayName = "Subject",
                MessageTemplate = "Raiding Subject now!",
                Kind = ShoutoutOverrideKinds.Raid,
            }
        );
        await db.SaveChangesAsync();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.Value.Overrides.ShoutoutMessageTemplate.Should().Be("Go check out Subject!");
        result.Value.Overrides.RaidMessageTemplate.Should().Be("Raiding Subject now!");
    }

    [Fact]
    public async Task GetProfileAsync_FoldsCommandUsage_CountAndLastUsed()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        await SeedSubjectAsync(db);

        DateTime older = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime newer = new(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc);
        db.CommandUsages.Add(
            new()
            {
                BroadcasterId = Broadcaster,
                CommandNameSnapshot = "hug",
                ViewerProfileId = Guid.CreateVersion7(),
                ViewerUserId = Subject,
                WasSuccessful = true,
                CreatedAt = older,
            }
        );
        db.CommandUsages.Add(
            new()
            {
                BroadcasterId = Broadcaster,
                CommandNameSnapshot = "dance",
                ViewerProfileId = Guid.CreateVersion7(),
                ViewerUserId = Subject,
                WasSuccessful = true,
                CreatedAt = newer,
            }
        );
        await db.SaveChangesAsync();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.Value.CommandUsage.TotalCommandsUsed.Should().Be(2);
        result.Value.CommandUsage.LastUsedUtc.Should().Be(newer);
    }

    [Fact]
    public async Task GetProfileAsync_FoldsFreeFormViewerData_AsKeyValuePairs()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        await SeedSubjectAsync(db);

        db.ViewerData.Add(
            new ViewerDatum
            {
                BroadcasterId = Broadcaster,
                ViewerUserId = Subject,
                Key = "favorite_game",
                Value = "Hollow Knight",
            }
        );
        await db.SaveChangesAsync();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result
            .Value.FreeFormData.Should()
            .ContainSingle(d => d.Key == "favorite_game" && d.Value == "Hollow Knight");
    }

    [Fact]
    public async Task GetProfileAsync_ScopesEveryGroup_ToTheRequestedBroadcaster_NotAnotherChannelsRows()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        await SeedSubjectAsync(db);
        Guid otherChannel = Guid.CreateVersion7();

        // The same Subject has data in a DIFFERENT channel — none of it may leak into this channel's profile.
        db.CurrencyAccounts.Add(
            new()
            {
                BroadcasterId = otherChannel,
                ViewerUserId = Subject,
                ViewerTwitchUserId = "subject-twitch",
                Balance = 99999,
            }
        );
        db.UserModerationHistories.Add(
            new()
            {
                BroadcasterId = otherChannel,
                SubjectUserId = Subject,
                SubjectTwitchUserId = "subject-twitch",
                BanCount = 5,
            }
        );
        await db.SaveChangesAsync();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.Value.Economy.Wallet.Should().BeNull("that wallet belongs to a different channel");
        result
            .Value.ModerationHistory.Should()
            .BeNull("that history belongs to a different channel");
    }
}
