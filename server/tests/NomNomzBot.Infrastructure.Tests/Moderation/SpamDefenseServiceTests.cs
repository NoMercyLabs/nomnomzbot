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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// The spam-defence stack against a real database (spam-defense.md §L0–§L5, §6).
///
/// <para>These run on SQLite rather than a mocked context on purpose. The tier resolution counts
/// distinct active days with a date projection, and whether EF can translate that is a question only a
/// real provider answers — a faked context would pass while the live path threw.</para>
/// </summary>
public class SpamDefenseServiceTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0199c000-0000-7000-8000-0000000000d1");
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 22, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly FakeTimeProvider _time = new(Now);

    public SpamDefenseServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using AppDbContext db = NewDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
        db.Channels.Add(
            new Channel
            {
                Id = Channel,
                OwnerUserId = Guid.NewGuid(),
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "chan-ext",
                Name = "chan",
                NameNormalized = "chan",
            }
        );
        db.SaveChanges();
    }

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private SpamDefenseService NewService(AppDbContext db) => new(db, _time);

    private static SpamEvaluationRequest Message(
        string text,
        string userId = "viewer-1",
        bool isSubscriber = false,
        bool isModerator = false
    ) =>
        new(
            Channel,
            AuthEnums.Platform.Twitch,
            MessageId: Guid.NewGuid().ToString(),
            PlatformUserId: userId,
            DisplayName: "Viewer",
            Message: text,
            IsBroadcaster: false,
            IsModerator: isModerator,
            IsVip: false,
            IsSubscriber: isSubscriber
        );

    /// <summary>Give a viewer real history in this channel: <paramref name="days"/> separate days.</summary>
    private void SeedHistory(string userId, int days, int perDay)
    {
        using AppDbContext db = NewDbContext();
        for (int d = 0; d < days; d++)
        for (int i = 0; i < perDay; i++)
            db.ChatMessages.Add(
                new ChatMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    BroadcasterId = Channel,
                    UserId = userId,
                    Username = "viewer",
                    DisplayName = "Viewer",
                    UserType = "viewer",
                    Message = "hello",
                    CreatedAt = Now.UtcDateTime.AddDays(-400 + d),
                }
            );
        db.SaveChanges();
    }

    // ---- The wiring actually reaches the database -----------------------------------------------

    [Fact]
    public async Task ADetectedMessage_IsRecorded_WithTheFullExplanation()
    {
        // The whole point of the slice: an engine nothing calls protects nobody, and a verdict nobody
        // recorded cannot be reviewed.
        using AppDbContext db = NewDbContext();
        SpamEvaluationResult? result = await NewService(db)
            .EvaluateAsync(Message("f​r​ee f​ollows"));

        result.Should().NotBeNull();
        result!.DetectionId.Should().NotBeNull();

        using AppDbContext read = NewDbContext();
        SpamDetection stored = await read.SpamDetections.SingleAsync();

        stored.Confidence.Should().Be(SpamConfidence.High);
        stored.Signals.Should().Contain(nameof(ContentSignal.CosmeticAbuse));
        stored.Reason.Should().NotBeNullOrWhiteSpace("SD7: no black-box verdicts");
        stored.Skeleton.Should().NotBeNullOrWhiteSpace();
        stored.MessageText.Should().Contain("ollows", "a reviewer must see what was actually said");
        stored.DetectedAt.Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public async Task ANewChannelIsInDryRun_SoTheFirstMessageItEverSeesActionsNobody()
    {
        // No policy row exists. The defaults must be the safe ones, because this is the state every
        // channel is in on the day it installs the bot.
        using AppDbContext db = NewDbContext();
        SpamEvaluationResult? result = await NewService(db)
            .EvaluateAsync(Message("f​r​ee f​ollows"));

        result!.Decision.IsDryRun.Should().BeTrue();
        result.Decision.Outcome.Should().Be(SpamOutcome.None, "nothing happens");
        result
            .Decision.WouldHaveBeen.Should()
            .Be(SpamOutcome.DeleteAndEscalate, "but the record shows what would have");

        using AppDbContext read = NewDbContext();
        SpamDetection stored = await read.SpamDetections.SingleAsync();
        stored.WasDryRun.Should().BeTrue();
        stored.Outcome.Should().Be(SpamOutcome.None);
    }

    [Fact]
    public async Task OrdinaryChat_WritesNoRowAtAll()
    {
        // The detection log is for verdicts a human might review, not a second copy of chat. A row per
        // message would make the review queue useless and the table enormous.
        using AppDbContext db = NewDbContext();
        SpamDefenseService service = NewService(db);

        await service.EvaluateAsync(Message("hey chat how's everyone doing"));
        await service.EvaluateAsync(Message("gg that was insane"));

        using AppDbContext read = NewDbContext();
        (await read.SpamDetections.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TheKillSwitchIsRespected()
    {
        using (AppDbContext setup = NewDbContext())
            await NewService(setup)
                .UpdateSettingsAsync(Channel, new SpamDefenseSettings { IsEnabled = false });

        using AppDbContext db = NewDbContext();
        SpamEvaluationResult? result = await NewService(db)
            .EvaluateAsync(Message("f​r​ee f​ollows"));

        result.Should().BeNull();
        using AppDbContext read = NewDbContext();
        (await read.SpamDetections.CountAsync()).Should().Be(0);
    }

    // ---- Tier resolution runs against real history ----------------------------------------------

    [Fact]
    public async Task AThreeYearRegular_IsEstablished_AndIsNeverActionedEvenWithEnforcementOn()
    {
        // SD8, end to end and through the database: the distinct-active-day count is computed by EF
        // against SQLite, which is the part a mocked context would never have exercised.
        SeedHistory("regular-1", days: 40, perDay: 10);

        using (AppDbContext setup = NewDbContext())
            await NewService(setup)
                .UpdateSettingsAsync(Channel, new SpamDefenseSettings { DryRun = false });

        using AppDbContext db = NewDbContext();
        SpamEvaluationResult? result = await NewService(db)
            .EvaluateAsync(Message("f​r​ee f​ollows", userId: "regular-1"));

        result!.Tier.Should().Be(SpamTrustTier.Established);
        result.Decision.Outcome.Should().Be(SpamOutcome.Flag);
        result.Decision.TouchesAccount.Should().BeFalse();
    }

    [Fact]
    public async Task ABurstOfMessagesInTwoNights_DoesNotBuyImmunity()
    {
        // Same message count as the regular above, packed into two days. The distinct-active-days
        // requirement is what makes that a spammer's shape rather than a regular's — and it only works
        // if the query really does count distinct DAYS.
        SeedHistory("burst-1", days: 2, perDay: 200);

        using AppDbContext db = NewDbContext();
        SpamEvaluationResult? result = await NewService(db)
            .EvaluateAsync(Message("f​r​ee f​ollows", userId: "burst-1"));

        result!.Tier.Should().NotBe(SpamTrustTier.Established);
    }

    [Fact]
    public async Task AModerator_IsEstablishedFromTheirBadgeAlone_WithoutAQuery()
    {
        using AppDbContext db = NewDbContext();
        SpamEvaluationResult? result = await NewService(db)
            .EvaluateAsync(Message("f​r​ee f​ollows", isModerator: true));

        result!.Tier.Should().Be(SpamTrustTier.Established);
    }

    [Fact]
    public async Task ASubscriber_HasStanding_AndIsNeverAutoActionedOnTheAccount()
    {
        // §L1.2: subscriber anywhere on this instance is a floor, not a score.
        using (AppDbContext setup = NewDbContext())
            await NewService(setup)
                .UpdateSettingsAsync(Channel, new SpamDefenseSettings { DryRun = false });

        using AppDbContext db = NewDbContext();
        SpamEvaluationResult? result = await NewService(db)
            .EvaluateAsync(Message("f​r​ee f​ollows", isSubscriber: true));

        result!.Tier.Should().Be(SpamTrustTier.SemiTrusted);
        result.Decision.TouchesAccount.Should().BeFalse();
        result.Decision.Outcome.Should().Be(SpamOutcome.DeleteAndQueue);
    }

    // ---- Settings round-trip and validation ------------------------------------------------------

    [Fact]
    public async Task SettingsSurviveARoundTrip_UnchangedInEveryField()
    {
        // The row and the record are two shapes of one thing; if a field were dropped in conversion
        // the operator would set it, see it save, and find it reverted.
        SpamDefenseSettings edited = new()
        {
            DryRun = false,
            NearDuplicateSimilarity = 0.75,
            MinimumSkeletonLength = 12,
            NonLatinScriptGate = true,
            QualifyNoStandingShare = 0.9,
            DequalifyNoStandingShare = 0.5,
            MinimumCohortSize = 8,
            WindowSeconds = 900,
            MaxWindowSeconds = 2400,
            ActionDelaySeconds = 30,
            AutoReverseOnDequalify = false,
            FollowSpikeFactor = 7,
            JoinBurstFactor = 6,
            LockdownMinutes = 20,
            LockdownAutoExtend = false,
            LockdownMaxMinutes = 90,
            NetworkSubscribe = false,
            NetworkContribute = true,
            RequiredCorroborations = 5,
            SemiTrustedWatchHoursHere = 15,
            SemiTrustedWatchHoursInstance = 40,
        };

        using (AppDbContext write = NewDbContext())
            (await NewService(write).UpdateSettingsAsync(Channel, edited))
                .IsSuccess.Should()
                .BeTrue();

        using AppDbContext read = NewDbContext();
        SpamDefenseSettings loaded = await NewService(read).GetSettingsAsync(Channel);

        loaded.Should().BeEquivalentTo(edited);
    }

    [Fact]
    public async Task AnOutOfRangeValue_IsRejected_NamingTheControlByItsResourceKey()
    {
        // Ranges are enforced server-side (§6.1). The failure names the control by the key the
        // dashboard translates, so a Dutch operator is not shown an English sentence.
        using AppDbContext db = NewDbContext();
        Result<SpamDefenseSettings> result = await NewService(db)
            .UpdateSettingsAsync(Channel, new SpamDefenseSettings { MinimumCohortSize = 1 });

        result.IsFailure.Should().BeTrue();
        result.ErrorMessage.Should().Contain("spam_setting_minimum_cohort_size_label");
        result.ErrorCode.Should().Be("spam.setting.out_of_range");
    }

    [Fact]
    public async Task AnExonerationShareAboveTheCampaignShare_IsRejected()
    {
        // The hysteresis band is a safety property, not a preference: inverted, a cohort on the line
        // would flap between actioning people and reversing it.
        using AppDbContext db = NewDbContext();
        Result<SpamDefenseSettings> result = await NewService(db)
            .UpdateSettingsAsync(
                Channel,
                new SpamDefenseSettings
                {
                    QualifyNoStandingShare = 0.7,
                    DequalifyNoStandingShare = 0.8,
                }
            );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("spam.setting.dequalify_not_below_qualify");
    }

    [Fact]
    public async Task AnUnconfiguredChannel_ReadsTheShippedDefaults()
    {
        using AppDbContext db = NewDbContext();

        (await NewService(db).GetSettingsAsync(Channel)).Should().Be(new SpamDefenseSettings());
    }

    [Fact]
    public async Task EnablingTheStack_StartsTheSevenDayObservationClock()
    {
        // §6.2 — so the dashboard can answer "how long have I been watching?" rather than guessing.
        using AppDbContext db = NewDbContext();
        await NewService(db).UpdateSettingsAsync(Channel, new SpamDefenseSettings());

        using AppDbContext read = NewDbContext();
        SpamDefensePolicy policy = await read.SpamDefensePolicies.SingleAsync();

        policy.EnforcementEligibleAt.Should().Be(Now.UtcDateTime.AddDays(7));
    }

    public void Dispose() => _connection.Dispose();
}
