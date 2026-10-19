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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Community.Dtos;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Giveaways.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Domain.ViewerData.Entities;
using NomNomzBot.Infrastructure.Community;
using NomNomzBot.Infrastructure.Stream.RaidSuggestions;
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
    ) Build(IFollowStateService? follows = null, ISpamDefenseService? spam = null)
    {
        spam ??= Substitute.For<ISpamDefenseService>();
        if (follows is null)
        {
            follows = Substitute.For<IFollowStateService>();
            follows
                .ResolveAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(FollowLookup.Unknown);
        }

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

        ViewerProfileService sut = new(
            db,
            analytics,
            permits,
            quotes,
            tts,
            follows,
            spam,
            new RaidHistoryReader(db)
        );
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

    private static long _journalPosition;

    private static EventJournal RaidRow(Guid channel, string from, string to) =>
        new()
        {
            EventId = Guid.NewGuid(),
            BroadcasterId = channel,
            StreamPosition = Interlocked.Increment(ref _journalPosition),
            EventType = "channel.raid",
            EventVersion = 1,
            Source = "eventsub",
            Payload =
                $"{{\"from_broadcaster_user_id\":\"{from}\",\"to_broadcaster_user_id\":\"{to}\",\"viewers\":9}}",
            Metadata = "{}",
            OccurredAt = DateTime.UtcNow,
            RecordedAt = DateTime.UtcNow,
        };

    [Fact]
    public async Task GetProfileAsync_CountsRaidsInBothDirections_FromThisChannelsRaidRowsOnly()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        await SeedSubjectAsync(db);
        Guid otherChannel = Guid.CreateVersion7();
        db.Channels.Add(
            new()
            {
                Id = Broadcaster,
                OwnerUserId = Guid.CreateVersion7(),
                Provider = AuthEnums.Platform.Twitch,
                TwitchChannelId = "chan-twitch",
                ExternalChannelId = "chan-twitch",
                Name = "chan",
                NameNormalized = "chan",
            }
        );
        db.EventJournals.AddRange(
            RaidRow(Broadcaster, "subject-twitch", "chan-twitch"),
            RaidRow(Broadcaster, "subject-twitch", "chan-twitch"),
            RaidRow(Broadcaster, "subject-twitch", "chan-twitch"),
            RaidRow(Broadcaster, "chan-twitch", "subject-twitch"),
            RaidRow(Broadcaster, "chan-twitch", "someone-else"),
            RaidRow(Broadcaster, "someone-else", "chan-twitch"),
            RaidRow(otherChannel, "subject-twitch", "chan-twitch")
        );
        await db.SaveChangesAsync();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Raids.TimesRaidedThisChannel.Should().Be(3);
        result.Value.Raids.TimesThisChannelRaidedThem.Should().Be(1);
    }

    [Fact]
    public async Task GetProfileAsync_RaidCountsAreZero_WhenThereAreNoRaidRows()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        await SeedSubjectAsync(db);

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.Value.Raids.Should().Be(new ViewerRaidsDto(0, 0));
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

    [Theory]
    [InlineData(SpamTrustTier.Regular, "Regular")]
    [InlineData(SpamTrustTier.Untrusted, "Untrusted")]
    public async Task GetProfileAsync_CarriesTheBotsTrustTier_ResolvedForThisViewerAndChannel(
        SpamTrustTier tier,
        string expected
    )
    {
        ISpamDefenseService spam = Substitute.For<ISpamDefenseService>();
        spam.ResolveTrustTierAsync(
                Broadcaster,
                Arg.Any<string>(),
                "subject-twitch",
                Arg.Any<string>(),
                Arg.Any<bool>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(tier);
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build(
            spam: spam
        );
        await SeedSubjectAsync(db);

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.Value.Identity.TrustTier.Should().Be(expected);
    }

    [Fact]
    public async Task GetProfileAsync_CarriesEveryStoredIdentityFact_WithTheStoredValues()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        DateTime created = new(2014, 3, 9, 12, 30, 0, DateTimeKind.Utc);
        DateTime linked = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        DateTime login = new(2026, 9, 30, 8, 0, 0, DateTimeKind.Utc);
        db.Users.Add(
            new()
            {
                Id = Subject,
                TwitchUserId = "subject-twitch",
                Username = "subject",
                UsernameNormalized = "subject",
                DisplayName = "Subject",
                AccountCreatedAt = created,
                Description = "I play platformers.",
                BroadcasterType = "affiliate",
                Type = "global_mod",
                Color = "#FF4500",
                NickName = "Subby",
                Timezone = "Europe/Amsterdam",
                IsLurking = true,
                PronounManualOverride = true,
            }
        );
        db.UserIdentities.Add(
            new()
            {
                UserId = Subject,
                Provider = "twitch",
                ProviderUserId = "subject-twitch",
                ProviderUsername = "subject",
                IsPrimary = true,
                LinkedAt = linked,
                LastLoginAt = login,
            }
        );
        await db.SaveChangesAsync();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        ViewerIdentityDto identity = result.Value.Identity;
        identity.AccountCreatedAtUtc.Should().Be(created);
        identity.Description.Should().Be("I play platformers.");
        identity.BroadcasterType.Should().Be("affiliate");
        identity.UserType.Should().Be("global_mod");
        identity.ChatColor.Should().Be("#FF4500");
        identity.Nickname.Should().Be("Subby");
        identity.Timezone.Should().Be("Europe/Amsterdam");
        identity.IsLurking.Should().BeTrue();
        identity.PronounIsManualOverride.Should().BeTrue();
        LinkedIdentityDto link = identity.LinkedIdentities.Should().ContainSingle().Subject;
        link.LinkedAtUtc.Should().Be(linked);
        link.LastLoginAtUtc.Should().Be(login);
    }

    [Fact]
    public async Task GetProfileAsync_ChatColor_FallsBackToThisChannelsAssignedColor_WhenThePersonHasNone()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        await SeedSubjectAsync(db);
        db.ChatterAssignedColors.AddRange(
            new ChatterAssignedColor
            {
                BroadcasterId = Broadcaster,
                Provider = "twitch",
                UserId = "subject-twitch",
                ColorHex = "#1E90FF",
            },
            new ChatterAssignedColor
            {
                BroadcasterId = Guid.CreateVersion7(),
                Provider = "twitch",
                UserId = "subject-twitch",
                ColorHex = "#00FF7F",
            }
        );
        await db.SaveChangesAsync();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.Value.Identity.ChatColor.Should().Be("#1E90FF");
        result.Value.Identity.AccountCreatedAtUtc.Should().BeNull();
        result.Value.Identity.Nickname.Should().BeNull();
        result.Value.Identity.IsLurking.Should().BeFalse();
        result.Value.Identity.PronounIsManualOverride.Should().BeFalse();
    }

    [Fact]
    public async Task GetProfileAsync_Activity_SumsTheBitsThisPersonCheeredInThisChannelOnly()
    {
        (
            ViewerProfileService sut,
            ViewerProfileServiceTestDbContext db,
            IViewerAnalyticsService analytics,
            _,
            _,
            _
        ) = Build();
        await SeedSubjectAsync(db);
        analytics
            .GetProfileAsync(Broadcaster, Subject, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new ViewerProfileDto(
                        Subject,
                        "subject-twitch",
                        "Subject",
                        null,
                        null,
                        0,
                        7,
                        0,
                        0,
                        0,
                        false,
                        false,
                        null,
                        false
                    )
                )
            );
        Guid otherChannel = Guid.CreateVersion7();
        db.ChatMessages.AddRange(
            CheerLine("m1", Broadcaster, "subject-twitch", 100),
            CheerLine("m2", Broadcaster, "subject-twitch", 250),
            CheerLine("m3", Broadcaster, "subject-twitch", null),
            CheerLine("m4", Broadcaster, "someone-else", 5000),
            CheerLine("m5", otherChannel, "subject-twitch", 9000)
        );
        await db.SaveChangesAsync();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.Value.Activity.Should().NotBeNull();
        result.Value.Activity!.TotalBitsCheered.Should().Be(350);
        result.Value.Activity.TotalMessages.Should().Be(7);
    }

    private static ChatMessage CheerLine(string id, Guid broadcaster, string userId, int? bits) =>
        new()
        {
            Id = id,
            BroadcasterId = broadcaster,
            UserId = userId,
            Username = userId,
            DisplayName = userId,
            UserType = "",
            Message = "cheer",
            IsCheer = bits is not null,
            BitsAmount = bits,
        };

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
    public async Task GetProfileAsync_FoldsTheOneShoutoutLine_FromThePersonsOwnRow()
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
            }
        );
        await db.SaveChangesAsync();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.Value.Overrides.ShoutoutMessageTemplate.Should().Be("Go check out Subject!");
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
    public async Task GetProfileAsync_CommandUsage_CountsOnlySucceededRuns_LikeTheProfileCounter()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        await SeedSubjectAsync(db);

        DateTime succeededAt = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime failedAt = new(2026, 8, 9, 0, 0, 0, DateTimeKind.Utc);
        (string Name, bool Succeeded, DateTime At)[] runs =
        [
            ("hug", true, succeededAt),
            ("hug", true, succeededAt.AddHours(1)),
            ("dance", false, failedAt),
        ];
        foreach ((string name, bool succeeded, DateTime at) in runs)
        {
            db.CommandUsages.Add(
                new()
                {
                    BroadcasterId = Broadcaster,
                    CommandNameSnapshot = name,
                    ViewerProfileId = Guid.CreateVersion7(),
                    ViewerUserId = Subject,
                    WasSuccessful = succeeded,
                    CreatedAt = at,
                }
            );
        }
        await db.SaveChangesAsync();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        // ViewerProfileProjection bumps TotalCommandsUsed only for a succeeded run, so the list must too.
        result.Value.CommandUsage.TotalCommandsUsed.Should().Be(2);
        result.Value.CommandUsage.LastUsedUtc.Should().Be(succeededAt.AddHours(1));
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

    private static readonly DateTime ImportedAt = new(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc);

    private static async Task SeedImportedSubjectAsync(ViewerProfileServiceTestDbContext db)
    {
        await SeedSubjectAsync(db);
        User user = await db.Users.SingleAsync(u => u.Id == Subject);
        user.CreatedAt = ImportedAt;
        await db.SaveChangesAsync();
    }

    private static ChatMessage ChatLine(Guid channel, DateTime at, string id)
    {
        return new()
        {
            Id = id,
            BroadcasterId = channel,
            UserId = "subject-twitch",
            Username = "subject",
            DisplayName = "Subject",
            UserType = "viewer",
            Message = "hi",
            CreatedAt = at,
        };
    }

    [Fact]
    public async Task GetProfileAsync_FirstSeen_IsTheFirstChatLineInThisChannel_WhenOlderThanTheUserRow()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        await SeedImportedSubjectAsync(db);
        DateTime first = new(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        db.ChatMessages.Add(ChatLine(Broadcaster, first.AddDays(30), "m2"));
        db.ChatMessages.Add(ChatLine(Broadcaster, first, "m1"));
        await db.SaveChangesAsync();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.Value.Identity.FirstSeenUtc.Should().Be(first);
    }

    [Fact]
    public async Task GetProfileAsync_FirstSeen_IsTheAnalyticsFirstSeen_WhenOlderThanTheFirstChatLine()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        await SeedImportedSubjectAsync(db);
        DateTime analyticsFirst = new(2024, 1, 5, 8, 0, 0, DateTimeKind.Utc);
        db.ChatMessages.Add(ChatLine(Broadcaster, analyticsFirst.AddYears(1), "m1"));
        db.ViewerProfiles.Add(
            new()
            {
                BroadcasterId = Broadcaster,
                ViewerUserId = Subject,
                ViewerTwitchUserId = "subject-twitch",
                FirstSeenAt = analyticsFirst,
            }
        );
        await db.SaveChangesAsync();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.Value.Identity.FirstSeenUtc.Should().Be(analyticsFirst);
    }

    [Fact]
    public async Task GetProfileAsync_FirstSeen_IgnoresChatAndAnalyticsFromAnotherChannel()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        await SeedImportedSubjectAsync(db);
        Guid otherChannel = Guid.CreateVersion7();
        DateTime thisChannelLine = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        db.ChatMessages.Add(
            ChatLine(otherChannel, new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), "o1")
        );
        db.ViewerProfiles.Add(
            new()
            {
                BroadcasterId = otherChannel,
                ViewerUserId = Subject,
                ViewerTwitchUserId = "subject-twitch",
                FirstSeenAt = new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            }
        );
        db.ChatMessages.Add(ChatLine(Broadcaster, thisChannelLine, "t1"));
        await db.SaveChangesAsync();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.Value.Identity.FirstSeenUtc.Should().Be(thisChannelLine);
    }

    [Fact]
    public async Task GetProfileAsync_FirstSeen_FallsBackToTheUserRow_WhenNoChannelFactExists()
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build();
        await SeedImportedSubjectAsync(db);

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.Value.Identity.FirstSeenUtc.Should().Be(ImportedAt);
    }

    private static IFollowStateService FollowReturning(FollowLookup lookup)
    {
        IFollowStateService follows = Substitute.For<IFollowStateService>();
        follows
            .ResolveAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(lookup);
        return follows;
    }

    [Fact]
    public async Task GetProfileAsync_FollowedAt_CarriesTheLookupDate_AsUtc()
    {
        DateTimeOffset followedAt = new(2024, 3, 9, 14, 30, 0, TimeSpan.FromHours(2));
        IFollowStateService follows = FollowReturning(
            new FollowLookup(FollowState.Following, followedAt)
        );
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build(
            follows
        );
        await SeedSubjectAsync(db);

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        DateTime? actual = result.Value.Identity.FollowedAtUtc;
        actual.Should().Be(new DateTime(2024, 3, 9, 12, 30, 0, DateTimeKind.Utc));
        actual.GetValueOrDefault().Kind.Should().Be(DateTimeKind.Utc);
        await follows
            .Received(1)
            .ResolveAsync(
                Broadcaster,
                AuthEnums.Platform.Twitch,
                "subject-twitch",
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(FollowState.NotFollowing)]
    [InlineData(FollowState.Unknown)]
    public async Task GetProfileAsync_FollowedAt_IsNull_WhenTheLookupHasNoFollow(FollowState state)
    {
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build(
            FollowReturning(new FollowLookup(state, null))
        );
        await SeedSubjectAsync(db);

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.IsSuccess.Should().BeTrue();
        result.Value.Identity.FollowedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task GetProfileAsync_FollowedAt_IsNull_AndTheProfileStillLoads_WhenTheLookupThrows()
    {
        IFollowStateService follows = Substitute.For<IFollowStateService>();
        follows
            .ResolveAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<FollowLookup>>(_ => throw new HttpRequestException("helix down"));
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build(
            follows
        );
        await SeedSubjectAsync(db);

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.IsSuccess.Should().BeTrue();
        result.Value.Identity.Username.Should().Be("subject");
        result.Value.Identity.FollowedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task GetProfileAsync_FollowedAt_IsNull_WithoutCallingTheLookup_WhenThePersonHasNoTwitchId()
    {
        IFollowStateService follows = FollowReturning(
            new FollowLookup(FollowState.Following, DateTimeOffset.UtcNow)
        );
        (ViewerProfileService sut, ViewerProfileServiceTestDbContext db, _, _, _, _) = Build(
            follows
        );
        db.Users.Add(
            new()
            {
                Id = Subject,
                TwitchUserId = null,
                Username = "subject",
                UsernameNormalized = "subject",
                DisplayName = "Subject",
            }
        );
        await db.SaveChangesAsync();

        Result<ViewerProfileSummaryDto> result = await sut.GetProfileAsync(Broadcaster, Subject);

        result.Value.Identity.FollowedAtUtc.Should().BeNull();
        await follows.DidNotReceiveWithAnyArgs().ResolveAsync(default, default!, default!);
    }
}
