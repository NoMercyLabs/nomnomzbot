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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.BackgroundServices;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Moderation;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// "Is protection running?" for one channel, over a real database. Each check is proven by the rows that
/// decide it: the stored bot-moderator status, the spam-defence policy, the connected platforms and the
/// EventSub registry. A check that cannot be read is unknown, never ok.
/// </summary>
public sealed class ProtectionStatusServiceTests : IDisposable
{
    private static readonly Guid ChannelId = Guid.Parse("0199c000-0000-7000-8000-0000000000e1");
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private const string BotTwitchId = "900";
    private const string BotLogin = "nomz_bot";

    private static readonly string[] ModerationTopics =
    [
        "channel.moderate",
        "automod.message.hold",
        "channel.suspicious_user.message",
        "channel.suspicious_user.update",
        "channel.unban_request.create",
    ];

    private readonly SqliteConnection _connection;
    private readonly FakeTimeProvider _time = new(Now);

    public ProtectionStatusServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using AppDbContext db = NewDb();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewDb()
    {
        DbContextOptionsBuilder<AppDbContext> options =
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection);
        return new(options.Options);
    }

    private ProtectionStatusService NewService(AppDbContext db, ISpamDefenseService? spam = null) =>
        new(
            db,
            new BotModeratorStatusReader(db, new ChannelTwitchBotResolver(db)),
            spam
                ?? new SpamDefenseService(
                    db,
                    _time,
                    Substitute.For<IModerationService>(),
                    new CurrentTenantService(),
                    Substitute.For<ITwitchUsersApi>(),
                    Substitute.For<IFollowStateService>()
                ),
            _time
        );

    /// <summary>A healthy Twitch channel: moderator bot, acting spam defence, every moderation topic enabled.</summary>
    private void SeedHealthyTwitchChannel(
        bool? botIsModerator = true,
        bool dryRun = false,
        DateTime? eligibleAt = null,
        bool spamEnabled = true,
        IEnumerable<string>? enabledTopics = null
    )
    {
        using AppDbContext db = NewDb();
        db.Channels.Add(
            new Channel
            {
                Id = ChannelId,
                OwnerUserId = Guid.NewGuid(),
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "chan-ext",
                Name = "chan",
                NameNormalized = "chan",
                BotIsModerator = botIsModerator,
                BotModeratorStatusBotUserId = botIsModerator is null ? null : BotTwitchId,
                BotModeratorStatusChangedAt = botIsModerator is null ? null : Now.UtcDateTime,
                CreatedAt = Now.UtcDateTime.AddDays(-30),
            }
        );
        db.BotAccounts.Add(
            new()
            {
                IdentityType = AuthEnums.BotIdentityType.Shared,
                Platform = AuthEnums.Platform.Twitch,
                BotUserId = BotTwitchId,
                BotUsername = BotLogin,
                ConnectionId = Guid.NewGuid(),
            }
        );
        db.SpamDefensePolicies.Add(
            new()
            {
                BroadcasterId = ChannelId,
                IsEnabled = spamEnabled,
                DryRun = dryRun,
                EnforcementEligibleAt = eligibleAt ?? Now.UtcDateTime.AddDays(-1),
            }
        );
        foreach (string topic in enabledTopics ?? ModerationTopics)
            db.EventSubSubscriptions.Add(Subscription(topic));
        db.SaveChanges();
    }

    private static EventSubSubscription Subscription(
        string topic,
        bool enabled = true,
        string status = "enabled"
    ) =>
        new()
        {
            BroadcasterId = ChannelId,
            EventType = topic,
            Version = "1",
            Enabled = enabled,
            Status = status,
        };

    private void AddConnection(string provider)
    {
        using AppDbContext db = NewDb();
        db.PlatformConnections.Add(
            new()
            {
                ChannelId = ChannelId,
                Provider = provider,
                ExternalChannelId = $"{provider}-ext",
                DisplayName = provider,
            }
        );
        db.SaveChanges();
    }

    private async Task<List<ProtectionCheckDto>> ChecksAsync(ISpamDefenseService? spam = null)
    {
        await using AppDbContext db = NewDb();
        Result<ProtectionStatusDto> result = await NewService(db, spam).GetAsync(ChannelId);
        result.IsSuccess.Should().BeTrue();
        return [.. result.Value.Checks];
    }

    private static ProtectionCheckDto Check(
        List<ProtectionCheckDto> checks,
        string key,
        string? platform = null
    ) => checks.Should().ContainSingle(c => c.Key == key && c.Platform == platform).Subject;

    [Fact]
    public async Task A_fully_protected_twitch_channel_reports_every_check_ok()
    {
        SeedHealthyTwitchChannel();
        AddConnection(AuthEnums.Platform.Twitch);

        List<ProtectionCheckDto> checks = await ChecksAsync();

        checks
            .Select(c => (c.Key, c.State, c.Platform))
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    (ProtectionCheckKeys.BotModerator, ProtectionCheckStates.Ok, "twitch"),
                    (ProtectionCheckKeys.SpamDefenseMode, ProtectionCheckStates.Ok, (string?)null),
                    (ProtectionCheckKeys.AutomaticAction, ProtectionCheckStates.Ok, "twitch"),
                    (ProtectionCheckKeys.EventSubModeration, ProtectionCheckStates.Ok, "twitch"),
                }!,
                o => o.WithStrictOrdering()
            );
        checks.Should().OnlyContain(c => c.Reason.Length > 0);
    }

    [Fact]
    public async Task A_bot_that_is_not_a_moderator_is_a_warning_naming_the_bot()
    {
        SeedHealthyTwitchChannel(botIsModerator: false);
        AddConnection(AuthEnums.Platform.Twitch);

        ProtectionCheckDto check = Check(
            await ChecksAsync(),
            ProtectionCheckKeys.BotModerator,
            "twitch"
        );

        check.State.Should().Be(ProtectionCheckStates.Warning);
        check.Reason.Should().Contain(BotLogin).And.Contain("moderator");
    }

    [Fact]
    public async Task A_bot_status_never_observed_is_unknown_not_ok()
    {
        SeedHealthyTwitchChannel(botIsModerator: null);
        AddConnection(AuthEnums.Platform.Twitch);

        ProtectionCheckDto check = Check(
            await ChecksAsync(),
            ProtectionCheckKeys.BotModerator,
            "twitch"
        );

        check.State.Should().Be(ProtectionCheckStates.Unknown);
        check.Reason.Should().Contain(BotLogin).And.Contain("not");
    }

    [Fact]
    public async Task Dry_run_is_a_warning_saying_actions_are_only_recorded()
    {
        SeedHealthyTwitchChannel(dryRun: true);
        AddConnection(AuthEnums.Platform.Twitch);

        ProtectionCheckDto check = Check(await ChecksAsync(), ProtectionCheckKeys.SpamDefenseMode);

        check.State.Should().Be(ProtectionCheckStates.Warning);
        check.Reason.Should().Contain("only recorded");
    }

    [Fact]
    public async Task The_seven_day_observation_window_is_a_warning_even_with_dry_run_off()
    {
        SeedHealthyTwitchChannel(dryRun: false, eligibleAt: Now.UtcDateTime.AddDays(3));
        AddConnection(AuthEnums.Platform.Twitch);

        ProtectionCheckDto check = Check(await ChecksAsync(), ProtectionCheckKeys.SpamDefenseMode);

        check.State.Should().Be(ProtectionCheckStates.Warning);
        check.Reason.Should().Contain("only recorded");
    }

    [Fact]
    public async Task Spam_defence_switched_off_is_a_warning_saying_so()
    {
        SeedHealthyTwitchChannel(spamEnabled: false);
        AddConnection(AuthEnums.Platform.Twitch);

        ProtectionCheckDto check = Check(await ChecksAsync(), ProtectionCheckKeys.SpamDefenseMode);

        check.State.Should().Be(ProtectionCheckStates.Warning);
        check.Reason.Should().Contain("switched off");
    }

    [Fact]
    public async Task A_spam_settings_read_that_throws_is_unknown_with_the_reason()
    {
        SeedHealthyTwitchChannel();
        AddConnection(AuthEnums.Platform.Twitch);
        ISpamDefenseService spam = Substitute.For<ISpamDefenseService>();
        spam.GetSettingsAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns<Task<NomNomzBot.Domain.Moderation.SpamDefense.SpamDefenseSettings>>(_ =>
                throw new InvalidOperationException("settings store down")
            );

        List<ProtectionCheckDto> checks = await ChecksAsync(spam);

        ProtectionCheckDto check = Check(checks, ProtectionCheckKeys.SpamDefenseMode);
        check.State.Should().Be(ProtectionCheckStates.Unknown);
        check.Reason.Should().Contain("settings store down");
        Check(checks, ProtectionCheckKeys.EventSubModeration, "twitch")
            .State.Should()
            .Be(ProtectionCheckStates.Ok);
    }

    [Fact]
    public async Task Automatic_action_is_reported_per_platform_and_warns_where_it_cannot_act()
    {
        SeedHealthyTwitchChannel();
        AddConnection(AuthEnums.Platform.Twitch);
        AddConnection(AuthEnums.Platform.YouTube);

        List<ProtectionCheckDto> checks = await ChecksAsync();

        Check(checks, ProtectionCheckKeys.AutomaticAction, "twitch")
            .State.Should()
            .Be(ProtectionCheckStates.Ok);
        ProtectionCheckDto youtube = Check(checks, ProtectionCheckKeys.AutomaticAction, "youtube");
        youtube.State.Should().Be(ProtectionCheckStates.Warning);
        youtube.Reason.Should().Contain("youtube").And.Contain("cannot");
    }

    [Fact]
    public async Task A_channel_without_connection_rows_is_judged_on_its_own_platform()
    {
        SeedHealthyTwitchChannel();

        List<ProtectionCheckDto> checks = await ChecksAsync();

        Check(checks, ProtectionCheckKeys.AutomaticAction, "twitch")
            .State.Should()
            .Be(ProtectionCheckStates.Ok);
    }

    [Fact]
    public async Task Missing_moderation_topics_fail_the_check_and_are_named()
    {
        SeedHealthyTwitchChannel(
            enabledTopics: ["channel.moderate", "channel.suspicious_user.update"]
        );
        using (AppDbContext db = NewDb())
        {
            db.EventSubSubscriptions.Add(Subscription("automod.message.hold", enabled: false));
            db.EventSubSubscriptions.Add(
                Subscription("channel.unban_request.create", status: "failed")
            );
            await db.SaveChangesAsync();
        }
        AddConnection(AuthEnums.Platform.Twitch);

        ProtectionCheckDto check = Check(
            await ChecksAsync(),
            ProtectionCheckKeys.EventSubModeration,
            "twitch"
        );

        check.State.Should().Be(ProtectionCheckStates.Failing);
        check
            .Reason.Should()
            .Contain("automod.message.hold")
            .And.Contain("channel.suspicious_user.message")
            .And.Contain("channel.unban_request.create")
            .And.NotContain("channel.moderate,")
            .And.NotContain("channel.suspicious_user.update");
    }

    [Fact]
    public async Task An_unreadable_subscription_registry_is_unknown_not_ok()
    {
        SeedHealthyTwitchChannel();
        AddConnection(AuthEnums.Platform.Twitch);
        using (AppDbContext db = NewDb())
            db.Database.ExecuteSqlRaw("DROP TABLE EventSubSubscriptions;");

        List<ProtectionCheckDto> checks = await ChecksAsync();

        ProtectionCheckDto check = Check(checks, ProtectionCheckKeys.EventSubModeration, "twitch");
        check.State.Should().Be(ProtectionCheckStates.Unknown);
        check.Reason.Should().NotBeNullOrWhiteSpace();
        Check(checks, ProtectionCheckKeys.BotModerator, "twitch")
            .State.Should()
            .Be(ProtectionCheckStates.Ok);
    }

    [Fact]
    public async Task A_channel_with_no_twitch_presence_gets_no_twitch_only_checks()
    {
        using (AppDbContext db = NewDb())
        {
            db.Channels.Add(
                new Channel
                {
                    Id = ChannelId,
                    OwnerUserId = Guid.NewGuid(),
                    Provider = AuthEnums.Platform.YouTube,
                    ExternalChannelId = "yt-ext",
                    Name = "chan",
                    NameNormalized = "chan",
                }
            );
            await db.SaveChangesAsync();
        }
        AddConnection(AuthEnums.Platform.YouTube);

        List<ProtectionCheckDto> checks = await ChecksAsync();

        checks
            .Select(c => c.Key)
            .Should()
            .BeEquivalentTo([
                ProtectionCheckKeys.SpamDefenseMode,
                ProtectionCheckKeys.AutomaticAction,
            ]);
        Check(checks, ProtectionCheckKeys.AutomaticAction, "youtube")
            .State.Should()
            .Be(ProtectionCheckStates.Warning);
    }

    [Fact]
    public async Task An_unknown_channel_is_a_not_found_failure()
    {
        await using AppDbContext db = NewDb();

        Result<ProtectionStatusDto> result = await NewService(db).GetAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("CHANNEL_NOT_FOUND");
    }

    [Fact]
    public void The_moderation_topics_are_topics_the_bot_really_subscribes_to()
    {
        BotLifecycleService
            .ChannelEventTypes.Should()
            .Contain(ModerationTopics)
            .And.Contain(ProtectionStatusService.ModerationTopics);
    }
}
