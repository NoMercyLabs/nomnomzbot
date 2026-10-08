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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Analytics.Entities;
using NomNomzBot.Domain.Chat.Entities;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Platform.Persistence.Interceptors;
using NSubstitute;

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

    /// <summary>
    /// With a <paramref name="tenant"/>, the context carries the production tenant-stamp interceptor, so a
    /// save behaves as it does inside a resolved-tenant request rather than in a bare unit test.
    /// </summary>
    private AppDbContext NewDbContext(ICurrentTenantService? tenant = null)
    {
        DbContextOptionsBuilder<AppDbContext> options =
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection);
        if (tenant is not null)
            options.AddInterceptors(new TenantStampInterceptor(tenant));
        return new(options.Options);
    }

    private SpamDefenseService NewService(
        AppDbContext db,
        IModerationService? moderation = null,
        ICurrentTenantService? tenant = null,
        ITwitchUsersApi? twitchUsers = null,
        IFollowStateService? follows = null
    ) =>
        new(
            db,
            _time,
            moderation ?? Substitute.For<IModerationService>(),
            tenant ?? new CurrentTenantService(),
            twitchUsers ?? Substitute.For<ITwitchUsersApi>(),
            follows ?? Substitute.For<IFollowStateService>()
        );

    /// <summary>An automatic (non-dry-run) escalation, the shape a moderator can actually overturn.</summary>
    private static SpamDetection AutomaticDetection(
        Guid broadcasterId,
        string subjectPlatformUserId,
        string displayName,
        DateTime detectedAt
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = broadcasterId,
            SubjectPlatformUserId = subjectPlatformUserId,
            SubjectDisplayName = displayName,
            Provider = AuthEnums.Platform.Twitch,
            MessageId = Guid.NewGuid().ToString(),
            MessageText = "free f0ll0ws check bio",
            Skeleton = "free follows check bio",
            Signals = nameof(SpamConfidence.High),
            Confidence = SpamConfidence.High,
            Tier = SpamTrustTier.Untrusted,
            Outcome = SpamOutcome.DeleteAndEscalate,
            WouldHaveBeen = SpamOutcome.DeleteAndEscalate,
            WasDryRun = false,
            Reason = "High confidence — routed to the escalation ladder.",
            DetectedAt = detectedAt,
        };

    private static SpamEvaluationRequest Message(
        string text,
        string userId = "viewer-1",
        bool isSubscriber = false,
        bool isModerator = false,
        string provider = AuthEnums.Platform.Twitch
    ) =>
        new(
            Channel,
            provider,
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
        result.DetectionId.Should().NotBeNull();

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

    // ---- The tier comes from real watch time, and capabilities must be earned -------------------

    private const string Link = "check out https://example.com/clip";
    private const string JapaneseChat = "こんにちは みなさん";

    /// <summary>Real watch time for a viewer in a channel, the way the watch projection stores it.</summary>
    private void SeedWatchHours(string userId, double hours, Guid? channel = null)
    {
        using AppDbContext db = NewDbContext();
        db.ViewerProfiles.Add(
            new ViewerProfile
            {
                BroadcasterId = channel ?? Channel,
                ViewerUserId = Guid.NewGuid(),
                ViewerTwitchUserId = userId,
                TotalWatchSeconds = (long)(hours * 3600),
            }
        );
        db.SaveChanges();
    }

    private async Task ConfigureAsync(SpamDefenseSettings settings)
    {
        using AppDbContext setup = NewDbContext();
        Result<SpamDefenseSettings> saved = await NewService(setup)
            .UpdateSettingsAsync(Channel, settings);
        saved.IsSuccess.Should().BeTrue();
    }

    private async Task<SpamEvaluationResult> EvaluateAsync(SpamEvaluationRequest request)
    {
        using AppDbContext db = NewDbContext();
        SpamEvaluationResult? result = await NewService(db).EvaluateAsync(request);
        return result!;
    }

    [Theory]
    [InlineData(10, SpamTrustTier.SemiTrusted)] // 12 watch hours here against a 10 hour bar
    [InlineData(20, SpamTrustTier.Untrusted)] // the same 12 hours against a 20 hour bar
    public async Task WatchHoursHere_EarnSemiTrusted_OnlyAgainstTheChannelsOwnThreshold(
        double thresholdHours,
        SpamTrustTier expected
    )
    {
        SeedWatchHours("watcher-1", hours: 12);
        await ConfigureAsync(
            new SpamDefenseSettings { SemiTrustedWatchHoursHere = thresholdHours }
        );

        SpamEvaluationResult result = await EvaluateAsync(Message("hello chat", "watcher-1"));

        result.Tier.Should().Be(expected);
    }

    [Fact]
    public async Task WatchHoursAcrossChannels_AreSummed_ForTheInstanceWideThreshold()
    {
        // 6 hours here and 6 hours in another channel: neither alone reaches the 10 hour bar, together
        // they reach 12. The here-threshold is raised out of reach so only the instance sum can earn it.
        Guid other = Guid.Parse("0199c000-0000-7000-8000-0000000000d2");
        SeedWatchHours("traveller-1", hours: 6);
        SeedWatchHours("traveller-1", hours: 6, channel: other);
        SeedWatchHours("homebody-1", hours: 6);
        await ConfigureAsync(
            new SpamDefenseSettings
            {
                SemiTrustedWatchHoursHere = 50,
                SemiTrustedWatchHoursInstance = 10,
            }
        );

        SpamEvaluationResult traveller = await EvaluateAsync(Message("hello", "traveller-1"));
        SpamEvaluationResult homebody = await EvaluateAsync(Message("hello", "homebody-1"));

        traveller.Tier.Should().Be(SpamTrustTier.SemiTrusted);
        homebody.Tier.Should().Be(SpamTrustTier.Untrusted);
    }

    [Fact]
    public async Task ALinkFromAnUntrustedViewer_WithNoOtherSignal_IsDeletedAndQueued_WithTheReason()
    {
        await ConfigureAsync(new SpamDefenseSettings { DryRun = false });

        SpamEvaluationResult result = await EvaluateAsync(Message(Link, "stranger-1"));

        result.Tier.Should().Be(SpamTrustTier.Untrusted);
        result.Decision.Outcome.Should().Be(SpamOutcome.DeleteAndQueue);
        result.Decision.TouchesAccount.Should().BeFalse();

        using AppDbContext read = NewDbContext();
        SpamDetection stored = await read.SpamDetections.SingleAsync();
        stored.Confidence.Should().Be(SpamConfidence.Medium);
        stored.Outcome.Should().Be(SpamOutcome.DeleteAndQueue);
        stored.WouldHaveBeen.Should().Be(SpamOutcome.DeleteAndQueue);
        stored.WasDryRun.Should().BeFalse();
        stored.Signals.Should().Be(nameof(ContentSignal.UnearnedCapability));
        stored.Reason.Should().Contain(nameof(SpamCapability.PostLink));
        stored
            .Reason.Should()
            .Contain(nameof(SpamTrustTier.Known), "the reason names the tier that earns it");
    }

    [Fact]
    public async Task TheSameLink_FromAViewerWhoEarnedPostLink_ProducesNoDetection()
    {
        SeedWatchHours("watcher-2", hours: 12);
        await ConfigureAsync(new SpamDefenseSettings { DryRun = false });

        SpamEvaluationResult result = await EvaluateAsync(Message(Link, "watcher-2"));

        result.Tier.Should().Be(SpamTrustTier.SemiTrusted);
        result.Decision.Outcome.Should().Be(SpamOutcome.None);
        result.DetectionId.Should().BeNull();
        using AppDbContext read = NewDbContext();
        (await read.SpamDetections.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AnUnearnedLink_NeverEscalatesBeyondTheContentsOwnConfidence()
    {
        // The message is already High on its own (cosmetic abuse). The unearned link must not change
        // that, and the tier floor never lowers it either.
        await ConfigureAsync(new SpamDefenseSettings { DryRun = false });

        SpamEvaluationResult result = await EvaluateAsync(
            Message("f​r​ee https://example.com/x", "stranger-2")
        );

        result.Confidence.Should().Be(SpamConfidence.High);
        using AppDbContext read = NewDbContext();
        SpamDetection stored = await read.SpamDetections.SingleAsync();
        stored.Confidence.Should().Be(SpamConfidence.High);
        stored.Outcome.Should().Be(SpamOutcome.DeleteAndEscalate);
    }

    [Fact]
    public async Task AnUnearnedLink_LiftsAWeakSignalToMedium_AndKeepsBothSignalsOnTheRow()
    {
        // "promo @someone" is a single weak signal (Low, flag only). The unearned link lifts it to
        // Medium, which is delete-and-queue, still never the account.
        await ConfigureAsync(new SpamDefenseSettings { DryRun = false });

        SpamEvaluationResult result = await EvaluateAsync(
            Message("promo @someone https://example.com", "stranger-3")
        );

        result.Confidence.Should().Be(SpamConfidence.Medium);
        using AppDbContext read = NewDbContext();
        SpamDetection stored = await read.SpamDetections.SingleAsync();
        stored.Signals.Should().Contain(nameof(ContentSignal.PromoShape));
        stored.Signals.Should().Contain(nameof(ContentSignal.UnearnedCapability));
        stored.Outcome.Should().Be(SpamOutcome.DeleteAndQueue);
    }

    [Fact]
    public async Task AnEstablishedViewersLink_IsNotQuestioned()
    {
        // SD8: immunity is a short-circuit, so the capability floor never runs for an Established viewer.
        SeedHistory("regular-9", days: 40, perDay: 10);
        await ConfigureAsync(new SpamDefenseSettings { DryRun = false });

        SpamEvaluationResult result = await EvaluateAsync(Message(Link, "regular-9"));

        result.Tier.Should().Be(SpamTrustTier.Established);
        result.Decision.Outcome.Should().Be(SpamOutcome.None);
        using AppDbContext read = NewDbContext();
        (await read.SpamDetections.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task NonLatinScript_IsFlagged_OnlyWhenTheChannelTurnsTheGateOn()
    {
        await ConfigureAsync(
            new SpamDefenseSettings { DryRun = false, NonLatinScriptGate = false }
        );
        SpamEvaluationResult gateOff = await EvaluateAsync(Message(JapaneseChat, "reader-1"));

        gateOff.Decision.Outcome.Should().Be(SpamOutcome.None);
        using (AppDbContext read = NewDbContext())
            (await read.SpamDetections.CountAsync()).Should().Be(0);

        await ConfigureAsync(new SpamDefenseSettings { DryRun = false, NonLatinScriptGate = true });
        SpamEvaluationResult gateOn = await EvaluateAsync(Message(JapaneseChat, "reader-1"));

        gateOn.Decision.Outcome.Should().Be(SpamOutcome.DeleteAndQueue);
        using AppDbContext after = NewDbContext();
        SpamDetection stored = await after.SpamDetections.SingleAsync();
        stored.Reason.Should().Contain(nameof(SpamCapability.NonLatinScript));
        stored.Signals.Should().Be(nameof(ContentSignal.UnearnedCapability));
    }

    [Fact]
    public async Task NonLatinScript_WithTheGateOn_IsAllowedForAViewerWhoHasStanding()
    {
        SeedWatchHours("watcher-3", hours: 12);
        await ConfigureAsync(new SpamDefenseSettings { DryRun = false, NonLatinScriptGate = true });

        SpamEvaluationResult result = await EvaluateAsync(Message(JapaneseChat, "watcher-3"));

        result.Decision.Outcome.Should().Be(SpamOutcome.None);
        result.DetectionId.Should().BeNull();
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
        result.ErrorCode.Should().Be("VALIDATION_FAILED", "so the API maps it to 400, not 500");
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
        result.ErrorCode.Should().Be("VALIDATION_FAILED", "so the API maps it to 400, not 500");
        result.ErrorMessage.Should().Contain("spam_setting_dequalify_below_qualify");
    }

    [Fact]
    public async Task AnUnconfiguredChannel_ReadsTheShippedDefaults()
    {
        using AppDbContext db = NewDbContext();

        (await NewService(db).GetSettingsAsync(Channel)).Should().Be(new SpamDefenseSettings());
    }

    [Fact]
    public async Task EnablingTheStackAfterItWasOff_StartsTheSevenDayObservationClockNow()
    {
        // §6.2 — so the dashboard can answer "how long have I been watching?" rather than guessing.
        using AppDbContext db = NewDbContext();
        SpamDefenseService service = NewService(db);
        await service.UpdateSettingsAsync(Channel, new SpamDefenseSettings { IsEnabled = false });
        await service.UpdateSettingsAsync(Channel, new SpamDefenseSettings());

        using AppDbContext read = NewDbContext();
        SpamDefensePolicy policy = await read.SpamDefensePolicies.SingleAsync();

        policy.EnforcementEligibleAt.Should().Be(Now.UtcDateTime.AddDays(7));
    }

    [Fact]
    public async Task AChannelTrackingTheDefaults_KeepsTheClockItStartedAtOnboarding_OnItsFirstSave()
    {
        SetChannelCreatedAt(Now.UtcDateTime.AddDays(-2));

        using AppDbContext db = NewDbContext();
        await NewService(db).UpdateSettingsAsync(Channel, new SpamDefenseSettings());

        using AppDbContext read = NewDbContext();
        (await read.SpamDefensePolicies.SingleAsync())
            .EnforcementEligibleAt.Should()
            .Be(Now.UtcDateTime.AddDays(5));
    }

    [Fact]
    public async Task TurningDryRunOffInsideTheSevenDayWindow_StillOnlyObserves()
    {
        SetChannelCreatedAt(Now.UtcDateTime.AddDays(-2));
        using (AppDbContext setup = NewDbContext())
            await NewService(setup)
                .UpdateSettingsAsync(Channel, new SpamDefenseSettings { DryRun = false });

        using AppDbContext db = NewDbContext();
        SpamEvaluationResult? result = await NewService(db)
            .EvaluateAsync(Message("f​r​ee f​ollows"));

        result!.Decision.IsDryRun.Should().BeTrue();
        result.Decision.Outcome.Should().Be(SpamOutcome.None);
        result.Decision.WouldHaveBeen.Should().Be(SpamOutcome.DeleteAndEscalate);
        using AppDbContext read = NewDbContext();
        (await read.SpamDetections.SingleAsync()).WasDryRun.Should().BeTrue();
    }

    [Fact]
    public async Task OnceTheSevenDayWindowHasPassed_TheSameChannelActs()
    {
        SetChannelCreatedAt(Now.UtcDateTime.AddDays(-2));
        using (AppDbContext setup = NewDbContext())
            await NewService(setup)
                .UpdateSettingsAsync(Channel, new SpamDefenseSettings { DryRun = false });
        _time.Advance(TimeSpan.FromDays(6));

        using AppDbContext db = NewDbContext();
        SpamEvaluationResult? result = await NewService(db)
            .EvaluateAsync(Message("f​r​ee f​ollows"));

        result!.Decision.IsDryRun.Should().BeFalse();
        result.Decision.Outcome.Should().Be(SpamOutcome.DeleteAndEscalate);
    }

    [Fact]
    public async Task ThePolicyPage_ShowsTheWindowOfAChannelThatNeverSaved()
    {
        SetChannelCreatedAt(Now.UtcDateTime.AddDays(-2));

        using AppDbContext db = NewDbContext();
        SpamDefensePolicyDto policy = await NewService(db).GetPolicyAsync(Channel);

        policy.EnforcementEligibleAt.Should().Be(Now.UtcDateTime.AddDays(5));
    }

    [Fact]
    public async Task ThePolicyPage_MarksASettingNothingReadsYet_AsNotActive_WithItsReasonAndSlice()
    {
        using AppDbContext db = NewDbContext();
        SpamDefensePolicyDto policy = await NewService(db).GetPolicyAsync(Channel);

        SpamSettingDescriptorDto lockdown = policy.Catalogue.Single(d =>
            d.Key == nameof(SpamDefenseSettings.LockdownMinutes)
        );
        SpamSettingDescriptorDto dryRun = policy.Catalogue.Single(d =>
            d.Key == nameof(SpamDefenseSettings.DryRun)
        );

        lockdown.PendingSlice.Should().Be("S-SPAM-LOCKDOWN-WIRE");
        lockdown.InactiveReasonKey.Should().Be("spam_setting_lockdown_minutes_inactive");
        dryRun.PendingSlice.Should().BeNull();
        dryRun.InactiveReasonKey.Should().BeNull();
    }

    private void SetChannelCreatedAt(DateTime createdAt)
    {
        using AppDbContext db = NewDbContext();
        db.Channels.Where(c => c.Id == Channel)
            .ExecuteUpdate(s => s.SetProperty(c => c.CreatedAt, createdAt));
    }

    // ---- Campaigns and follow-bot blocks ---------------------------------------------------------

    private static readonly Guid OtherChannel = Guid.Parse("0199c000-0000-7000-8000-0000000000d9");

    /// <summary>A Twitch users API whose unblock succeeds unless the test says otherwise.</summary>
    private static ITwitchUsersApi UnblockingTwitch()
    {
        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();
        twitch
            .UnblockUserAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        return twitch;
    }

    private void SeedBlocks(
        Guid batch,
        int count,
        Guid channel,
        bool restored = false,
        bool dryRun = false
    )
    {
        using AppDbContext db = NewDbContext();
        for (int i = 0; i < count; i++)
            db.FollowBotBlocks.Add(
                new FollowBotBlock
                {
                    BroadcasterId = channel,
                    BatchId = batch,
                    SubjectPlatformUserId = $"bot{batch:N}-{i}",
                    SubjectUsername = $"viewer{i}8042193",
                    Indicators = nameof(FollowBotIndicator.GeneratedHandlePattern),
                    BatchExamined = count + 5,
                    BlockedAt = Now.UtcDateTime,
                    RestoredAt = restored ? Now.UtcDateTime : null,
                    WasDryRun = dryRun,
                }
            );
        db.SaveChanges();
    }

    [Fact]
    public async Task RestoringABatch_UnblocksEachAccountOnTwitch_BeforeStampingIt()
    {
        // The bug this pins: restore used to stamp RestoredAt and report success while the accounts
        // stayed blocked on Twitch. The Twitch call is the restore; the stamp only records it.
        Guid batch = Guid.NewGuid();
        SeedBlocks(batch, 3, Channel);
        ITwitchUsersApi twitch = UnblockingTwitch();

        using AppDbContext db = NewDbContext();
        Result<int> result = await NewService(db, twitchUsers: twitch)
            .RestoreFollowBotBatchAsync(Channel, batch);

        result.Value.Should().Be(3);
        foreach (int i in new[] { 0, 1, 2 })
            await twitch
                .Received(1)
                .UnblockUserAsync(Channel, $"bot{batch:N}-{i}", Arg.Any<CancellationToken>());
        twitch.ReceivedCalls().Should().HaveCount(3, "one unblock per block and nothing else");
    }

    [Fact]
    public async Task AnUnblockTwitchRefuses_StaysUnstamped_AndIsNotCountedAsRestored()
    {
        Guid batch = Guid.NewGuid();
        SeedBlocks(batch, 3, Channel);
        ITwitchUsersApi twitch = UnblockingTwitch();
        twitch
            .UnblockUserAsync(Channel, $"bot{batch:N}-1", Arg.Any<CancellationToken>())
            .Returns(Result.Failure("twitch said no"));

        using AppDbContext db = NewDbContext();
        Result<int> result = await NewService(db, twitchUsers: twitch)
            .RestoreFollowBotBatchAsync(Channel, batch);

        result.Value.Should().Be(2);
        using AppDbContext read = NewDbContext();
        List<FollowBotBlock> blocks = await read
            .FollowBotBlocks.Where(b => b.BatchId == batch)
            .OrderBy(b => b.SubjectPlatformUserId)
            .ToListAsync();
        blocks
            .Where(b => b.RestoredAt is null)
            .Select(b => b.SubjectPlatformUserId)
            .Should()
            .Equal($"bot{batch:N}-1");
        blocks.Count(b => b.RestoredAt is not null).Should().Be(2);
    }

    [Fact]
    public async Task ABatchWhereEveryUnblockFails_ReportsFailure_AndStampsNothing()
    {
        Guid batch = Guid.NewGuid();
        SeedBlocks(batch, 2, Channel);
        ITwitchUsersApi twitch = Substitute.For<ITwitchUsersApi>();
        twitch
            .UnblockUserAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure("twitch is down"));

        using AppDbContext db = NewDbContext();
        Result<int> result = await NewService(db, twitchUsers: twitch)
            .RestoreFollowBotBatchAsync(Channel, batch);

        result.IsFailure.Should().BeTrue("claiming a restore that restored nothing is a quiet lie");
        using AppDbContext read = NewDbContext();
        (await read.FollowBotBlocks.ToListAsync()).Should().OnlyContain(b => b.RestoredAt == null);
    }

    [Fact]
    public async Task ADryRunRow_IsStamped_WithoutAnyTwitchCall()
    {
        // A dry-run row never blocked anybody, so there is nothing to undo on Twitch.
        Guid batch = Guid.NewGuid();
        SeedBlocks(batch, 2, Channel, dryRun: true);
        ITwitchUsersApi twitch = UnblockingTwitch();

        using AppDbContext db = NewDbContext();
        Result<int> result = await NewService(db, twitchUsers: twitch)
            .RestoreFollowBotBatchAsync(Channel, batch);

        result.Value.Should().Be(2);
        twitch.ReceivedCalls().Should().BeEmpty();
        using AppDbContext read = NewDbContext();
        (await read.FollowBotBlocks.ToListAsync()).Should().OnlyContain(b => b.RestoredAt != null);
    }

    [Fact]
    public async Task RestoringABatch_RestoresEveryBlockInIt_AndNothingOutsideIt()
    {
        // The distinction a count-only assertion would let collapse: restoring "some blocks" is not
        // restoring THIS batch. A misread viral moment and a genuine farm can be minutes apart.
        Guid misread = Guid.NewGuid();
        Guid genuine = Guid.NewGuid();
        SeedBlocks(misread, 5, Channel);
        SeedBlocks(genuine, 3, Channel);

        using AppDbContext db = NewDbContext();
        Result<int> result = await NewService(db, twitchUsers: UnblockingTwitch())
            .RestoreFollowBotBatchAsync(Channel, misread);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(5);

        using AppDbContext read = NewDbContext();
        (await read.FollowBotBlocks.Where(b => b.BatchId == misread).ToListAsync())
            .Should()
            .OnlyContain(b => b.RestoredAt != null);
        (await read.FollowBotBlocks.Where(b => b.BatchId == genuine).ToListAsync())
            .Should()
            .OnlyContain(b => b.RestoredAt == null, "the other sweep must be untouched");
    }

    [Fact]
    public async Task ABatchFromAnotherChannel_IsNotRestorable()
    {
        // Cross-tenant: the query ignores the ambient filter (it runs outside a resolved-tenant
        // request), so the broadcaster has to be matched explicitly or one channel could undo
        // another's moderation.
        Guid batch = Guid.NewGuid();
        SeedBlocks(batch, 4, OtherChannel);

        using AppDbContext db = NewDbContext();
        Result<int> result = await NewService(db).RestoreFollowBotBatchAsync(Channel, batch);

        result.IsFailure.Should().BeTrue();
        using AppDbContext read = NewDbContext();
        (await read.FollowBotBlocks.ToListAsync()).Should().OnlyContain(b => b.RestoredAt == null);
    }

    [Fact]
    public async Task RestoringAnAlreadyRestoredBatch_ReportsFailureRatherThanClaimingSuccess()
    {
        // Reporting "restored 0" as success is the kind of quiet lie that makes an operator think an
        // action worked. There is nothing left to restore, and the answer says so.
        Guid batch = Guid.NewGuid();
        SeedBlocks(batch, 3, Channel, restored: true);

        using AppDbContext db = NewDbContext();
        (await NewService(db).RestoreFollowBotBatchAsync(Channel, batch))
            .IsFailure.Should()
            .BeTrue();
    }

    [Fact]
    public async Task EveryStoredBlockCarriesItsOwnEvidence()
    {
        // SD9 at the storage layer: a block that cannot say why is one nobody can review, and the
        // Follow-bot blocks surface exists precisely so somebody can.
        SeedBlocks(Guid.NewGuid(), 3, Channel);

        using AppDbContext db = NewDbContext();
        IReadOnlyList<FollowBotBlockDto> blocks = await NewService(db)
            .GetFollowBotBlocksAsync(Channel);

        blocks.Should().HaveCount(3);
        blocks.Should().OnlyContain(b => b.Indicators != "");
        // The denominator matters as much as the blocks: it is how an operator sees the sweep examined
        // more accounts than it acted on, which is SD9 holding in a form somebody can check.
        blocks
            .Should()
            .OnlyContain(
                b => b.BatchExamined > 3,
                "the sweep examined more accounts than it blocked"
            );
    }

    [Fact]
    public async Task BlocksAndCampaignsFromAnotherChannel_AreNeverListed()
    {
        SeedBlocks(Guid.NewGuid(), 2, OtherChannel);
        using (AppDbContext seed = NewDbContext())
        {
            seed.SpamCampaigns.Add(
                new SpamCampaignRecord
                {
                    BroadcasterId = OtherChannel,
                    Skeleton = "bestviewers",
                    Verdict = CohortVerdict.Campaign,
                    FirstSeenAt = Now.UtcDateTime,
                    LastSeenAt = Now.UtcDateTime,
                }
            );
            seed.SaveChanges();
        }

        using AppDbContext db = NewDbContext();
        SpamDefenseService service = NewService(db);

        (await service.GetFollowBotBlocksAsync(Channel)).Should().BeEmpty();
        (await service.GetCampaignsAsync(Channel)).Should().BeEmpty();
    }

    [Fact]
    public async Task CampaignsComeBackNewestFirst_WithTheNumbersTheVerdictTurnedOn()
    {
        // Ordering is a distinction a "returns 2 rows" assertion would miss, and it is the one that
        // decides which incident an operator sees when they open the page during an attack.
        using (AppDbContext seed = NewDbContext())
        {
            seed.SpamCampaigns.AddRange(
                new SpamCampaignRecord
                {
                    BroadcasterId = Channel,
                    Skeleton = "older",
                    Verdict = CohortVerdict.CommunityPattern,
                    QualificationCount = 35,
                    ActionableCount = 20,
                    ActionedCount = 2,
                    NoStandingShare = 0.57,
                    MayContributeToNetwork = false,
                    ReversedAt = Now.UtcDateTime,
                    ReversalReason = "15 regulars joined this pattern; it is not spam.",
                    FirstSeenAt = Now.UtcDateTime.AddHours(-2),
                    LastSeenAt = Now.UtcDateTime.AddHours(-2),
                },
                new SpamCampaignRecord
                {
                    BroadcasterId = Channel,
                    Skeleton = "newer",
                    Verdict = CohortVerdict.Campaign,
                    QualificationCount = 20,
                    ActionableCount = 20,
                    ActionedCount = 20,
                    NoStandingShare = 1.0,
                    FirstSeenAt = Now.UtcDateTime,
                    LastSeenAt = Now.UtcDateTime,
                }
            );
            seed.SaveChanges();
        }

        using AppDbContext db = NewDbContext();
        IReadOnlyList<SpamCampaignDto> campaigns = await NewService(db).GetCampaignsAsync(Channel);

        campaigns.Select(c => c.Skeleton).Should().ContainInOrder("newer", "older");

        SpamCampaignDto exonerated = campaigns.Single(c => c.Skeleton == "older");
        exonerated.Verdict.Should().Be(CohortVerdict.CommunityPattern);
        exonerated.ReversedAt.Should().NotBeNull();
        exonerated.ReversalReason.Should().Contain("regulars");
        exonerated
            .MayContributeToNetwork.Should()
            .BeFalse("a cohort that included standing viewers is never a network signature");
    }

    [Fact]
    public async Task CampaignsReportThreeDistinctReversalStates_NeverPartialAndFull()
    {
        // The dashboard must be able to tell "nobody tried" from "tried and fell short" from "fully
        // undone" — a partial restore left silent is exactly the defect this closes (20694769).
        using (AppDbContext seed = NewDbContext())
        {
            seed.SpamCampaigns.AddRange(
                new SpamCampaignRecord
                {
                    BroadcasterId = Channel,
                    Skeleton = "never-attempted",
                    Verdict = CohortVerdict.Campaign,
                    ActionedAccountIds = "bot-a,bot-b",
                    ActionedCount = 2,
                    ReversedAt = null,
                    ReversedByActorId = null,
                    RestoredAccountCount = 0,
                    RestorationFailedAccountIds = string.Empty,
                    FirstSeenAt = Now.UtcDateTime,
                    LastSeenAt = Now.UtcDateTime,
                },
                new SpamCampaignRecord
                {
                    BroadcasterId = Channel,
                    Skeleton = "partial-restore",
                    Verdict = CohortVerdict.Campaign,
                    ActionedAccountIds = "bot-c,bot-d,bot-e",
                    ActionedCount = 3,
                    ReversedAt = null,
                    ReversedByActorId = "system:spam-dequalify",
                    RestoredAccountCount = 1,
                    RestorationFailedAccountIds = "bot-d,bot-e",
                    FirstSeenAt = Now.UtcDateTime,
                    LastSeenAt = Now.UtcDateTime,
                },
                new SpamCampaignRecord
                {
                    BroadcasterId = Channel,
                    Skeleton = "fully-restored",
                    Verdict = CohortVerdict.CommunityPattern,
                    ActionedAccountIds = "bot-f,bot-g",
                    ActionedCount = 2,
                    ReversedAt = Now.UtcDateTime,
                    ReversalReason = "Regulars joined the pattern.",
                    ReversedByActorId = "system:spam-dequalify",
                    RestoredAccountCount = 2,
                    RestorationFailedAccountIds = string.Empty,
                    FirstSeenAt = Now.UtcDateTime,
                    LastSeenAt = Now.UtcDateTime,
                }
            );
            seed.SaveChanges();
        }

        using AppDbContext db = NewDbContext();
        IReadOnlyList<SpamCampaignDto> campaigns = await NewService(db).GetCampaignsAsync(Channel);

        SpamCampaignDto never = campaigns.Single(c => c.Skeleton == "never-attempted");
        never.ReversedAt.Should().BeNull();
        never.ReversedByActorId.Should().BeNull();
        never.RestoredAccountCount.Should().Be(0);
        never.RestorationFailedAccountIds.Should().BeEmpty();

        SpamCampaignDto partial = campaigns.Single(c => c.Skeleton == "partial-restore");
        partial
            .ReversedAt.Should()
            .BeNull("a partial restore must never claim a complete reversal");
        partial.ReversedByActorId.Should().Be("system:spam-dequalify");
        partial.RestoredAccountCount.Should().Be(1);
        partial.RestorationFailedAccountIds.Should().Contain("bot-d").And.Contain("bot-e");

        SpamCampaignDto full = campaigns.Single(c => c.Skeleton == "fully-restored");
        full.ReversedAt.Should().NotBeNull();
        full.ReversedByActorId.Should().Be("system:spam-dequalify");
        full.RestoredAccountCount.Should().Be(2);
        full.RestorationFailedAccountIds.Should().BeEmpty();

        // The three states must be pairwise distinguishable by a client rendering off the DTO alone.
        never
            .Should()
            .NotBeEquivalentTo(partial, "never-attempted and partial must render differently");
        partial
            .Should()
            .NotBeEquivalentTo(full, "partial and fully-restored must render differently");
        never
            .Should()
            .NotBeEquivalentTo(full, "never-attempted and fully-restored must render differently");
    }

    // ---- Platform defaults -----------------------------------------------------------------------

    [Fact]
    public async Task AnUnconfiguredChannel_TracksThePlatformDefaults_NotTheShippedConstants()
    {
        // §6: a channel setting left untouched TRACKS the default and moves when the default moves.
        // Falling straight through to the shipped constants would make the admin page a lie — an
        // operator would change a default and see nothing happen anywhere.
        using (AppDbContext admin = NewDbContext())
            await NewService(admin)
                .UpdateSettingsAsync(
                    SpamDefenseService.PlatformDefaultsScope,
                    new SpamDefenseSettings { ActionDelaySeconds = 45, MinimumCohortSize = 9 }
                );

        using AppDbContext db = NewDbContext();
        SpamDefenseSettings effective = await NewService(db).GetSettingsAsync(Channel);

        effective.ActionDelaySeconds.Should().Be(45);
        effective.MinimumCohortSize.Should().Be(9);
    }

    [Fact]
    public async Task AChannelThatPinnedItsOwnValue_IgnoresALaterDefaultChange()
    {
        // The other half of the same rule: editing PINS it. A channel that made a deliberate choice
        // must not have it silently overwritten by a platform-wide edit.
        using (AppDbContext own = NewDbContext())
            await NewService(own)
                .UpdateSettingsAsync(Channel, new SpamDefenseSettings { ActionDelaySeconds = 12 });

        using (AppDbContext admin = NewDbContext())
            await NewService(admin)
                .UpdateSettingsAsync(
                    SpamDefenseService.PlatformDefaultsScope,
                    new SpamDefenseSettings { ActionDelaySeconds = 45 }
                );

        using AppDbContext db = NewDbContext();
        (await NewService(db).GetSettingsAsync(Channel))
            .ActionDelaySeconds.Should()
            .Be(12, "the channel pinned its own value");
    }

    [Fact]
    public async Task ThePlatformDefaultsSave_KeepsTheSentinel_WhenTheAdminRequestCarriesTheirOwnChannel()
    {
        // A6 admin truth: an admin who owns a channel reaches the admin route with that channel as the
        // ambient tenant. The tenant-stamp interceptor would rewrite the new defaults row to it, so the
        // "platform defaults" the admin just saved would silently become their own channel's policy.
        CurrentTenantService tenant = new();
        tenant.SetTenant(Channel);
        using (AppDbContext admin = NewDbContext(tenant))
        {
            Result<SpamDefenseSettings> saved = await NewService(admin, tenant: tenant)
                .UpdateSettingsAsync(
                    SpamDefenseService.PlatformDefaultsScope,
                    new SpamDefenseSettings { ActionDelaySeconds = 45 }
                );
            saved.IsSuccess.Should().BeTrue();
        }

        using AppDbContext db = NewDbContext();
        List<Guid> scopes = await db
            .SpamDefensePolicies.IgnoreQueryFilters()
            .Select(p => p.BroadcasterId)
            .ToListAsync();
        scopes
            .Should()
            .Equal(
                [SpamDefenseService.PlatformDefaultsScope],
                "the defaults row must keep the sentinel, not land on the admin's channel"
            );
        tenant.BroadcasterId.Should().Be(Channel, "the ambient tenant is restored after the save");
        (await NewService(db).GetSettingsAsync(Channel))
            .ActionDelaySeconds.Should()
            .Be(45, "the untouched channel tracks the defaults that were just saved");
    }

    [Fact]
    public async Task TheChannelPage_ShowsThePlatformDefaultsItTracks_NotTheShippedConstants()
    {
        // The dashboard must show what actually runs for the channel: the platform defaults it tracks,
        // still marked as not pinned, rather than shipped constants the runtime no longer applies.
        using (AppDbContext admin = NewDbContext())
            await NewService(admin)
                .UpdateSettingsAsync(
                    SpamDefenseService.PlatformDefaultsScope,
                    new SpamDefenseSettings { ActionDelaySeconds = 45 }
                );

        using AppDbContext db = NewDbContext();
        SpamDefensePolicyDto page = await NewService(db).GetPolicyAsync(Channel);

        page.Settings.ActionDelaySeconds.Should().Be(45);
        page.IsPinned.Should().BeFalse("tracking the defaults is not a decision the channel made");
    }

    [Fact]
    public async Task ThePlatformDefaultsAreValidatedLikeAnyChannel()
    {
        // A default nobody could save on a channel page must not be settable platform-wide either —
        // otherwise the admin page becomes the way to put every channel into a state its own editor
        // rejects.
        using AppDbContext db = NewDbContext();
        Result<SpamDefenseSettings> result = await NewService(db)
            .UpdateSettingsAsync(
                SpamDefenseService.PlatformDefaultsScope,
                new SpamDefenseSettings { MinimumCohortSize = 1 }
            );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task TheDefaultsScopeIsNotARealChannel()
    {
        // If the sentinel could collide with a tenant, one channel's settings would silently become
        // everybody's.
        SpamDefenseService.PlatformDefaultsScope.Should().Be(Guid.Empty);
        SpamDefenseService.PlatformDefaultsScope.Should().NotBe(Channel);

        using AppDbContext db = NewDbContext();
        (await db.Channels.AnyAsync(c => c.Id == SpamDefenseService.PlatformDefaultsScope))
            .Should()
            .BeFalse();
    }

    // ---- Overturn reverses the REAL effect, not merely the row ------------------------------------

    [Fact]
    public async Task Overturn_CallsUnban_AndOnlyThenStampsTheDetectionReversed()
    {
        Guid operatorUserId = Guid.Parse("0199c000-0000-7000-8000-0000000000ee");
        using AppDbContext seed = NewDbContext();
        SpamDetection detection = AutomaticDetection(Channel, "bot-9", "Bot9", Now.UtcDateTime);
        seed.SpamDetections.Add(detection);
        seed.SaveChanges();

        IModerationService moderation = Substitute.For<IModerationService>();
        moderation
            .UnbanAsync(
                Channel.ToString(),
                operatorUserId,
                "bot-9",
                operatorUserId.ToString(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new ModerationActionResult(true, "unbanned")));

        using AppDbContext db = NewDbContext();
        Result overturn = await NewService(db, moderation)
            .OverturnDetectionAsync(Channel, detection.Id, operatorUserId);

        overturn.IsSuccess.Should().BeTrue();
        await moderation
            .Received(1)
            .UnbanAsync(
                Channel.ToString(),
                operatorUserId,
                "bot-9",
                operatorUserId.ToString(),
                Arg.Any<CancellationToken>()
            );

        using AppDbContext readBack = NewDbContext();
        SpamDetection stored = await readBack.SpamDetections.SingleAsync(d => d.Id == detection.Id);
        stored.OverturnedAt.Should().NotBeNull();
        stored
            .OverturnedByUserId.Should()
            .Be(operatorUserId, "the reversal must name an accountable operator");
    }

    [Fact]
    public async Task Overturn_WhenTheRealReversalFails_LeavesTheDetectionUntouched()
    {
        Guid operatorUserId = Guid.Parse("0199c000-0000-7000-8000-0000000000ef");
        using AppDbContext seed = NewDbContext();
        SpamDetection detection = AutomaticDetection(Channel, "bot-10", "Bot10", Now.UtcDateTime);
        seed.SpamDetections.Add(detection);
        seed.SaveChanges();

        IModerationService moderation = Substitute.For<IModerationService>();
        moderation
            .UnbanAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<ModerationActionResult>("Twitch is unreachable.", "transport"));

        using AppDbContext db = NewDbContext();
        Result overturn = await NewService(db, moderation)
            .OverturnDetectionAsync(Channel, detection.Id, operatorUserId);

        overturn.IsFailure.Should().BeTrue();
        overturn.ErrorCode.Should().Be("REVERSAL_FAILED");

        using AppDbContext readBack = NewDbContext();
        SpamDetection stored = await readBack.SpamDetections.SingleAsync(d => d.Id == detection.Id);
        stored
            .OverturnedAt.Should()
            .BeNull("a claim of reversal must never outrun what actually happened");
        stored.OverturnedByUserId.Should().BeNull();
    }

    [Fact]
    public async Task Overturn_NamesTheActingOperatorAsTheModeratorSigningTheReversal()
    {
        // The reversal is audited by naming the operator to Twitch's own moderator_id (so Twitch's mod
        // ledger and the local ModerationService record both attribute it), and separately by the
        // OverturnedByUserId stamp this test asserts.
        Guid operatorUserId = Guid.Parse("0199c000-0000-7000-8000-0000000000f0");
        using AppDbContext seed = NewDbContext();
        SpamDetection detection = AutomaticDetection(Channel, "bot-11", "Bot11", Now.UtcDateTime);
        seed.SpamDetections.Add(detection);
        seed.SaveChanges();

        IModerationService moderation = Substitute.For<IModerationService>();
        moderation
            .UnbanAsync(
                Arg.Any<string>(),
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new ModerationActionResult(true, "unbanned")));

        using AppDbContext db = NewDbContext();
        await NewService(db, moderation)
            .OverturnDetectionAsync(Channel, detection.Id, operatorUserId);

        await moderation
            .Received(1)
            .UnbanAsync(
                Arg.Any<string>(),
                operatorUserId,
                Arg.Any<string>(),
                operatorUserId.ToString(),
                Arg.Any<CancellationToken>()
            );

        using AppDbContext readBack = NewDbContext();
        (await readBack.SpamDetections.SingleAsync(d => d.Id == detection.Id))
            .OverturnedByUserId.Should()
            .Be(operatorUserId);
    }

    // ---- Follow age feeds the trust ladder -------------------------------------------------------

    private const string SpamText = "f​r​ee f​ollows";

    private static ITwitchChannelsApi FollowerApi(DateTimeOffset? followedAt)
    {
        ITwitchChannelsApi api = Substitute.For<ITwitchChannelsApi>();
        TwitchChannelFollower? follower = followedAt is null
            ? null
            : new("v1", "viewer", "Viewer", followedAt.Value);
        api.GetChannelFollowerAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(follower));
        return api;
    }

    private async Task<SpamTrustTier> TierOf(
        ITwitchChannelsApi api,
        FollowStateCache cache,
        string userId,
        string provider = AuthEnums.Platform.Twitch
    )
    {
        using AppDbContext db = NewDbContext();
        SpamEvaluationResult? result = await NewService(
                db,
                follows: new FollowStateService(api, cache)
            )
            .EvaluateAsync(Message(SpamText, userId: userId, provider: provider));
        return result!.Tier;
    }

    [Fact]
    public async Task ATenDayFollower_WithAMonthOfHistory_ReachesKnown()
    {
        SeedHistory("follower-1", days: 2, perDay: 3);
        ITwitchChannelsApi api = FollowerApi(Now.AddDays(-10));

        SpamTrustTier tier = await TierOf(api, new FollowStateCache(_time), "follower-1");

        tier.Should().Be(SpamTrustTier.Known);
    }

    [Fact]
    public async Task ATwoHourFollower_StaysBelowNewcomer()
    {
        SeedHistory("follower-2", days: 2, perDay: 3);
        ITwitchChannelsApi api = FollowerApi(Now.AddHours(-2));

        SpamTrustTier tier = await TierOf(api, new FollowStateCache(_time), "follower-2");

        tier.Should().Be(SpamTrustTier.Untrusted);
    }

    [Fact]
    public async Task AFailedLookup_EarnsNoFollowTier_AndIsNotCachedAsNotFollowing()
    {
        SeedHistory("follower-3", days: 2, perDay: 3);
        ITwitchChannelsApi api = Substitute.For<ITwitchChannelsApi>();
        api.GetChannelFollowerAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Failure<TwitchChannelFollower?>("missing moderator:read:followers", "SCOPE")
            );
        FollowStateCache cache = new(_time);

        SpamTrustTier tier = await TierOf(api, cache, "follower-3");

        tier.Should().Be(SpamTrustTier.Untrusted);
        cache.TryGet(Channel, "follower-3", out FollowLookup stored).Should().BeTrue();
        stored
            .State.Should()
            .Be(FollowState.Unknown, "a failed lookup is not evidence of not following");
    }

    [Fact]
    public async Task ASecondMessageFromTheSameViewer_MakesNoSecondHelixCall()
    {
        SeedHistory("follower-4", days: 2, perDay: 3);
        ITwitchChannelsApi api = FollowerApi(Now.AddDays(-10));
        FollowStateCache cache = new(_time);

        await TierOf(api, cache, "follower-4");
        SpamTrustTier second = await TierOf(api, cache, "follower-4");

        second.Should().Be(SpamTrustTier.Known);
        await api.Received(1)
            .GetChannelFollowerAsync(Channel, "follower-4", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFollowEvent_IsSeenByTheNextMessage_WithoutAHelixCall()
    {
        SeedHistory("follower-5", days: 2, perDay: 3);
        ITwitchChannelsApi api = FollowerApi(null);
        FollowStateCache cache = new(_time);

        (await TierOf(api, cache, "follower-5")).Should().Be(SpamTrustTier.Untrusted);
        cache.TryGet(Channel, "follower-5", out FollowLookup before).Should().BeTrue();
        before.State.Should().Be(FollowState.NotFollowing);

        await new FollowStateCacheHandler(cache).HandleAsync(
            new FollowEvent
            {
                BroadcasterId = Channel,
                UserId = "follower-5",
                UserDisplayName = "Viewer",
                UserLogin = "viewer",
                FollowedAt = Now.AddDays(-10),
            }
        );

        (await TierOf(api, cache, "follower-5")).Should().Be(SpamTrustTier.Known);
        await api.Received(1)
            .GetChannelFollowerAsync(Channel, "follower-5", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AKickViewer_MakesNoHelixCall_AndEarnsNoFollowTier()
    {
        SeedHistory("kick-1", days: 2, perDay: 3);
        ITwitchChannelsApi api = FollowerApi(Now.AddDays(-10));

        SpamTrustTier tier = await TierOf(
            api,
            new FollowStateCache(_time),
            "kick-1",
            AuthEnums.Platform.Kick
        );

        tier.Should().Be(SpamTrustTier.Untrusted);
        await api.DidNotReceiveWithAnyArgs().GetChannelFollowerAsync(default, default!);
    }

    [Fact]
    public async Task AnAccountTooYoungForNewcomer_SkipsTheLookup()
    {
        using (AppDbContext db = NewDbContext())
        {
            db.Users.Add(
                new User
                {
                    TwitchUserId = "brand-new",
                    Username = "brandnew",
                    UsernameNormalized = "brandnew",
                    DisplayName = "BrandNew",
                    AccountCreatedAt = Now.UtcDateTime.AddDays(-2),
                }
            );
            db.SaveChanges();
        }
        ITwitchChannelsApi api = FollowerApi(Now.AddDays(-10));

        SpamTrustTier tier = await TierOf(api, new FollowStateCache(_time), "brand-new");

        tier.Should().Be(SpamTrustTier.Untrusted);
        await api.DidNotReceiveWithAnyArgs().GetChannelFollowerAsync(default, default!);
    }

    public void Dispose() => _connection.Dispose();
}
