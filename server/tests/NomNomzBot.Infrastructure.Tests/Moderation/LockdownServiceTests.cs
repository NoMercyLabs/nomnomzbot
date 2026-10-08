// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Moderation.Lockdown;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// Lockdown against a stateful fake of Twitch's room settings (spam-defense.md §L5.1). The fake applies
/// every patch the way Twitch does, so a test can assert both the exact call that was made and the room
/// the viewers would actually see afterwards. Every service call gets a FRESH service and DbContext:
/// the process that engaged a window may not be the one that restores it.
/// </summary>
public class LockdownServiceTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0199c000-0000-7000-8000-0000000000b1");
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 20, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly HashSet<string> AllowedModerationCalls =
    [
        nameof(ITwitchModerationApi.GetShieldModeStatusAsync),
        nameof(ITwitchModerationApi.UpdateShieldModeStatusAsync),
        nameof(ITwitchModerationApi.GetAutoModSettingsAsync),
        nameof(ITwitchModerationApi.UpdateAutoModSettingsAsync),
    ];

    private readonly SqliteConnection _connection;
    private readonly FakeTimeProvider _time = new(T0);
    private readonly CapturingEventBus _bus = new();
    private readonly ITwitchChatApi _chat = Substitute.For<ITwitchChatApi>();
    private readonly ITwitchModerationApi _moderation = Substitute.For<ITwitchModerationApi>();

    private SpamDefenseSettings _policy = new();
    private TwitchChatSettings _room = NewRoom();
    private bool _shieldOn;
    private TwitchAutoModSettings _autoMod = NewAutoMod(2);
    private Func<UpdateChatSettingsRequest, bool> _chatUpdateFails = _ => false;
    private Func<bool, bool> _shieldUpdateFails = _ => false;

    public LockdownServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using AppDbContext db = NewDbContext();
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");

        _chat
            .GetChatSettingsAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Result.Success(_room)));
        _chat
            .UpdateChatSettingsAsync(
                Channel,
                Arg.Any<UpdateChatSettingsRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                UpdateChatSettingsRequest patch = call.ArgAt<UpdateChatSettingsRequest>(1);
                if (_chatUpdateFails(patch))
                    return Task.FromResult(
                        Result.Failure<TwitchChatSettings>("twitch_unavailable", "UPSTREAM_ERROR")
                    );
                _room = _room with
                {
                    FollowerMode = patch.FollowerMode ?? _room.FollowerMode,
                    FollowerModeDuration = patch.FollowerModeDuration ?? _room.FollowerModeDuration,
                    SlowMode = patch.SlowMode ?? _room.SlowMode,
                    SlowModeWaitTime = patch.SlowModeWaitTime ?? _room.SlowModeWaitTime,
                    SubscriberMode = patch.SubscriberMode ?? _room.SubscriberMode,
                    UniqueChatMode = patch.UniqueChatMode ?? _room.UniqueChatMode,
                };
                // Twitch reports no duration or wait while the gate is off.
                _room = _room with
                {
                    FollowerModeDuration = _room.FollowerMode ? _room.FollowerModeDuration : null,
                    SlowModeWaitTime = _room.SlowMode ? _room.SlowModeWaitTime : null,
                };
                return Task.FromResult(Result.Success(_room));
            });

        _moderation
            .GetShieldModeStatusAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Result.Success(ShieldStatus())));
        _moderation
            .UpdateShieldModeStatusAsync(Channel, Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                bool wanted = call.ArgAt<bool>(1);
                if (_shieldUpdateFails(wanted))
                    return Task.FromResult(
                        Result.Failure<TwitchShieldModeStatus>(
                            "twitch_unavailable",
                            "UPSTREAM_ERROR"
                        )
                    );
                _shieldOn = wanted;
                return Task.FromResult(Result.Success(ShieldStatus()));
            });

        _moderation
            .GetAutoModSettingsAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(Result.Success(_autoMod)));
        _moderation
            .UpdateAutoModSettingsAsync(
                Channel,
                Arg.Any<UpdateAutoModSettingsRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                UpdateAutoModSettingsRequest patch = call.ArgAt<UpdateAutoModSettingsRequest>(1);
                _autoMod = patch.OverallLevel is not null
                    ? NewAutoMod(patch.OverallLevel.Value)
                    : _autoMod with
                    {
                        OverallLevel = null,
                        Aggression = patch.Aggression ?? _autoMod.Aggression,
                        Bullying = patch.Bullying ?? _autoMod.Bullying,
                        Disability = patch.Disability ?? _autoMod.Disability,
                        Misogyny = patch.Misogyny ?? _autoMod.Misogyny,
                        RaceEthnicityOrReligion =
                            patch.RaceEthnicityOrReligion ?? _autoMod.RaceEthnicityOrReligion,
                        SexBasedTerms = patch.SexBasedTerms ?? _autoMod.SexBasedTerms,
                        SexualitySexOrGender =
                            patch.SexualitySexOrGender ?? _autoMod.SexualitySexOrGender,
                        Swearing = patch.Swearing ?? _autoMod.Swearing,
                    };
                return Task.FromResult(Result.Success(_autoMod));
            });
    }

    private static TwitchChatSettings NewRoom() =>
        new("b", false, false, null, null, null, null, false, null, false, false);

    private static TwitchAutoModSettings NewAutoMod(int level) =>
        new("b", "m", level, level, level, level, level, level, level, level, level);

    private TwitchShieldModeStatus ShieldStatus() => new(_shieldOn, "m", "m", "m", null);

    private AppDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private async Task<T> RunAsync<T>(Func<LockdownService, Task<T>> action)
    {
        using AppDbContext db = NewDbContext();
        ISpamDefenseService spam = Substitute.For<ISpamDefenseService>();
        spam.GetSettingsAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(_policy));
        LockdownService service = new(
            db,
            spam,
            [new TwitchLockdownAdapter(_chat, _moderation)],
            _time,
            _bus,
            NullLogger<LockdownService>.Instance
        );
        return await action(service);
    }

    private Task<Result<LockdownWindowStatus>> EngageAsync(
        string platform,
        params LockdownControl[] requested
    ) =>
        RunAsync(s =>
            s.EngageAsync(Channel, platform, "hate raid: 40 fresh accounts, one phrase", requested)
        );

    private Task<int> RestoreDueAsync() => RunAsync(s => s.RestoreDueAsync());

    private List<LockdownWindowRecord> Windows()
    {
        using AppDbContext db = NewDbContext();
        return db.LockdownWindows.IgnoreQueryFilters().OrderBy(w => w.StartedAt).ToList();
    }

    private static List<EngagedControl> EngagedOf(LockdownWindowRecord window) =>
        JsonSerializer.Deserialize<List<EngagedControl>>(window.EngagedControlsJson, Json)!;

    private static List<LockdownControl> ControlsOf(string json) =>
        JsonSerializer.Deserialize<List<LockdownControl>>(json, Json)!;

    /// <summary>Lockdown tightens the room and never acts on a person.</summary>
    private void AssertNoActionAgainstAPerson()
    {
        _moderation
            .ReceivedCalls()
            .Select(c => c.GetMethodInfo().Name)
            .Should()
            .OnlyContain(name => AllowedModerationCalls.Contains(name));
        _chat
            .ReceivedCalls()
            .Select(c => c.GetMethodInfo().Name)
            .Should()
            .OnlyContain(name =>
                name == nameof(ITwitchChatApi.GetChatSettingsAsync)
                || name == nameof(ITwitchChatApi.UpdateChatSettingsAsync)
            );
    }

    [Fact]
    public async Task Engage_tightens_the_room_and_stores_each_controls_previous_value()
    {
        Result<LockdownWindowStatus> result = await EngageAsync(
            "twitch",
            LockdownControl.FollowersOnly,
            LockdownControl.SlowMode,
            LockdownControl.ShieldMode,
            LockdownControl.BlockedTerms
        );

        result.IsSuccess.Should().BeTrue();
        await _chat
            .Received(1)
            .UpdateChatSettingsAsync(
                Channel,
                new UpdateChatSettingsRequest(FollowerMode: true, FollowerModeDuration: 10),
                Arg.Any<CancellationToken>()
            );
        await _chat
            .Received(1)
            .UpdateChatSettingsAsync(
                Channel,
                new UpdateChatSettingsRequest(SlowMode: true, SlowModeWaitTime: 30),
                Arg.Any<CancellationToken>()
            );
        await _moderation
            .Received(1)
            .UpdateShieldModeStatusAsync(Channel, true, Arg.Any<CancellationToken>());
        _room.FollowerMode.Should().BeTrue();
        _room.SlowMode.Should().BeTrue();
        _shieldOn.Should().BeTrue();

        LockdownWindowRecord window = Windows().Should().ContainSingle().Subject;
        window.BroadcasterId.Should().Be(Channel);
        window.Platform.Should().Be("twitch");
        window.Trigger.Should().Be("hate raid: 40 fresh accounts, one phrase");
        window.StartedAt.Should().Be(T0.UtcDateTime);
        window.ExpiresAt.Should().Be(T0.UtcDateTime.AddMinutes(15));
        window.EndedAt.Should().BeNull();
        window.RestoredAt.Should().BeNull();
        EngagedOf(window)
            .Should()
            .BeEquivalentTo(
                new List<EngagedControl>
                {
                    new(LockdownControl.FollowersOnly, "{\"Enabled\":false,\"Amount\":0}"),
                    new(LockdownControl.SlowMode, "{\"Enabled\":false,\"Amount\":null}"),
                    new(LockdownControl.ShieldMode, "false"),
                },
                o => o.WithStrictOrdering()
            );
        ControlsOf(window.UnavailableControlsJson).Should().Equal(LockdownControl.BlockedTerms);
        ControlsOf(window.ApplyFailedControlsJson).Should().BeEmpty();
        result
            .Value.Engaged.Should()
            .Equal(
                LockdownControl.FollowersOnly,
                LockdownControl.SlowMode,
                LockdownControl.ShieldMode
            );
        result.Value.Unavailable.Should().Equal(LockdownControl.BlockedTerms);
        AssertNoActionAgainstAPerson();
    }

    [Fact]
    public async Task A_control_already_at_the_locked_value_is_not_engaged_and_never_touched_on_restore()
    {
        _room = _room with { SlowMode = true, SlowModeWaitTime = 60, UniqueChatMode = true };

        Result<LockdownWindowStatus> result = await EngageAsync(
            "twitch",
            LockdownControl.SlowMode,
            LockdownControl.UniqueChat,
            LockdownControl.SubscribersOnly
        );

        result.Value.Engaged.Should().Equal(LockdownControl.SubscribersOnly);
        result.Value.Unavailable.Should().BeEmpty();
        result.Value.ApplyFailed.Should().BeEmpty();
        await _chat
            .Received(1)
            .UpdateChatSettingsAsync(
                Channel,
                new UpdateChatSettingsRequest(SubscriberMode: true),
                Arg.Any<CancellationToken>()
            );
        await _chat
            .DidNotReceive()
            .UpdateChatSettingsAsync(
                Channel,
                Arg.Is<UpdateChatSettingsRequest>(r =>
                    r.SlowMode != null || r.UniqueChatMode != null
                ),
                Arg.Any<CancellationToken>()
            );

        _time.Advance(TimeSpan.FromMinutes(16));
        (await RestoreDueAsync()).Should().Be(1);

        _room.SubscriberMode.Should().BeFalse();
        _room.SlowMode.Should().BeTrue();
        _room.SlowModeWaitTime.Should().Be(60);
        _room.UniqueChatMode.Should().BeTrue();
        await _chat
            .DidNotReceive()
            .UpdateChatSettingsAsync(
                Channel,
                Arg.Is<UpdateChatSettingsRequest>(r =>
                    r.SlowMode != null || r.UniqueChatMode != null
                ),
                Arg.Any<CancellationToken>()
            );
        AssertNoActionAgainstAPerson();
    }

    [Fact]
    public async Task A_control_that_fails_to_apply_is_recorded_and_the_rest_still_apply()
    {
        _shieldUpdateFails = wanted => wanted;

        Result<LockdownWindowStatus> result = await EngageAsync(
            "twitch",
            LockdownControl.ShieldMode,
            LockdownControl.FollowersOnly,
            LockdownControl.SubscribersOnly
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.ApplyFailed.Should().Equal(LockdownControl.ShieldMode);
        result
            .Value.Engaged.Should()
            .Equal(LockdownControl.FollowersOnly, LockdownControl.SubscribersOnly);
        _room.FollowerMode.Should().BeTrue();
        _room.SubscriberMode.Should().BeTrue();
        _shieldOn.Should().BeFalse();

        LockdownWindowRecord window = Windows().Should().ContainSingle().Subject;
        ControlsOf(window.ApplyFailedControlsJson).Should().Equal(LockdownControl.ShieldMode);
        EngagedOf(window)
            .Select(e => e.Control)
            .Should()
            .Equal(LockdownControl.FollowersOnly, LockdownControl.SubscribersOnly);
        AssertNoActionAgainstAPerson();
    }

    [Fact]
    public async Task A_control_whose_current_value_cannot_be_read_is_not_touched()
    {
        _chat
            .GetChatSettingsAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(
                    Result.Failure<TwitchChatSettings>("twitch_unavailable", "UPSTREAM_ERROR")
                )
            );

        Result<LockdownWindowStatus> result = await EngageAsync(
            "twitch",
            LockdownControl.SlowMode,
            LockdownControl.ShieldMode
        );

        result.Value.ApplyFailed.Should().Equal(LockdownControl.SlowMode);
        result.Value.Engaged.Should().Equal(LockdownControl.ShieldMode);
        await _chat
            .DidNotReceive()
            .UpdateChatSettingsAsync(
                Channel,
                Arg.Any<UpdateChatSettingsRequest>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(60, 25)]
    [InlineData(20, 20)]
    public async Task Engaging_again_while_active_extends_the_window_up_to_the_ceiling(
        int maxMinutes,
        int expectedMinutes
    )
    {
        _policy = new SpamDefenseSettings { LockdownMinutes = 15, LockdownMaxMinutes = maxMinutes };
        await EngageAsync("twitch", LockdownControl.SlowMode);
        _time.Advance(TimeSpan.FromMinutes(10));

        Result<LockdownWindowStatus> again = await EngageAsync("twitch", LockdownControl.SlowMode);

        again.IsSuccess.Should().BeTrue();
        LockdownWindowRecord window = Windows().Should().ContainSingle().Subject;
        window.StartedAt.Should().Be(T0.UtcDateTime);
        window.ExpiresAt.Should().Be(T0.UtcDateTime.AddMinutes(expectedMinutes));
        again.Value.ExpiresAt.Should().Be(T0.AddMinutes(expectedMinutes));
        await _chat
            .Received(1)
            .UpdateChatSettingsAsync(
                Channel,
                Arg.Any<UpdateChatSettingsRequest>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Engaging_again_leaves_the_expiry_alone_when_auto_extend_is_off()
    {
        _policy = new SpamDefenseSettings { LockdownAutoExtend = false };
        await EngageAsync("twitch", LockdownControl.SlowMode);
        _time.Advance(TimeSpan.FromMinutes(10));

        await EngageAsync("twitch", LockdownControl.SlowMode);

        LockdownWindowRecord window = Windows().Should().ContainSingle().Subject;
        window.ExpiresAt.Should().Be(T0.UtcDateTime.AddMinutes(15));
    }

    [Fact]
    public async Task The_first_window_is_capped_by_the_ceiling_when_the_duration_exceeds_it()
    {
        _policy = new SpamDefenseSettings { LockdownMinutes = 45, LockdownMaxMinutes = 30 };

        await EngageAsync("twitch", LockdownControl.SlowMode);

        Windows().Single().ExpiresAt.Should().Be(T0.UtcDateTime.AddMinutes(30));
    }

    [Fact]
    public async Task Restore_puts_every_previous_value_back_and_only_then_stamps_restored_at()
    {
        _room = _room with { SubscriberMode = true };
        _autoMod = NewAutoMod(2);
        await EngageAsync(
            "twitch",
            LockdownControl.FollowersOnly,
            LockdownControl.SlowMode,
            LockdownControl.ShieldMode,
            LockdownControl.StrictAutoMod,
            LockdownControl.UniqueChat
        );
        _autoMod.OverallLevel.Should().Be(4);

        _time.Advance(TimeSpan.FromMinutes(5));
        (await RestoreDueAsync()).Should().Be(0);
        Windows().Single().RestoredAt.Should().BeNull();
        _room.FollowerMode.Should().BeTrue();

        _time.Advance(TimeSpan.FromMinutes(11));
        (await RestoreDueAsync()).Should().Be(1);

        _room.FollowerMode.Should().BeFalse();
        _room.SlowMode.Should().BeFalse();
        _room.UniqueChatMode.Should().BeFalse();
        _room.SubscriberMode.Should().BeTrue();
        _shieldOn.Should().BeFalse();
        _autoMod.OverallLevel.Should().Be(2);
        await _chat
            .Received(1)
            .UpdateChatSettingsAsync(
                Channel,
                new UpdateChatSettingsRequest(FollowerMode: false),
                Arg.Any<CancellationToken>()
            );
        await _moderation
            .Received(1)
            .UpdateAutoModSettingsAsync(
                Channel,
                new UpdateAutoModSettingsRequest(OverallLevel: 2),
                Arg.Any<CancellationToken>()
            );

        LockdownWindowRecord window = Windows().Single();
        window.RestoredAt.Should().Be(T0.UtcDateTime.AddMinutes(16));
        ControlsOf(window.RestorationFailedControlsJson).Should().BeEmpty();

        (await RestoreDueAsync()).Should().Be(0);
        AssertNoActionAgainstAPerson();
    }

    [Fact]
    public async Task Restore_gives_back_per_category_automod_levels_when_overall_was_not_set()
    {
        _autoMod = NewAutoMod(0) with { OverallLevel = null, Swearing = 3, Bullying = 1 };
        await EngageAsync("twitch", LockdownControl.StrictAutoMod);

        _time.Advance(TimeSpan.FromMinutes(16));
        await RestoreDueAsync();

        await _moderation
            .Received(1)
            .UpdateAutoModSettingsAsync(
                Channel,
                new UpdateAutoModSettingsRequest(
                    Aggression: 0,
                    Bullying: 1,
                    Disability: 0,
                    Misogyny: 0,
                    RaceEthnicityOrReligion: 0,
                    SexBasedTerms: 0,
                    SexualitySexOrGender: 0,
                    Swearing: 3
                ),
                Arg.Any<CancellationToken>()
            );
        _autoMod.OverallLevel.Should().BeNull();
        _autoMod.Swearing.Should().Be(3);
        _autoMod.Bullying.Should().Be(1);
    }

    [Fact]
    public async Task A_failed_restore_keeps_restored_at_empty_lists_the_control_and_retries_only_that_one()
    {
        await EngageAsync("twitch", LockdownControl.FollowersOnly, LockdownControl.ShieldMode);
        _shieldUpdateFails = wanted => !wanted;
        _time.Advance(TimeSpan.FromMinutes(16));

        (await RestoreDueAsync()).Should().Be(0);

        LockdownWindowRecord window = Windows().Single();
        window.RestoredAt.Should().BeNull();
        ControlsOf(window.RestorationFailedControlsJson).Should().Equal(LockdownControl.ShieldMode);
        _room.FollowerMode.Should().BeFalse();
        _shieldOn.Should().BeTrue();

        _shieldUpdateFails = _ => false;
        _time.Advance(TimeSpan.FromMinutes(1));
        (await RestoreDueAsync()).Should().Be(1);

        _shieldOn.Should().BeFalse();
        await _chat
            .Received(1)
            .UpdateChatSettingsAsync(
                Channel,
                new UpdateChatSettingsRequest(FollowerMode: false),
                Arg.Any<CancellationToken>()
            );
        window = Windows().Single();
        window.RestoredAt.Should().Be(T0.UtcDateTime.AddMinutes(17));
        ControlsOf(window.RestorationFailedControlsJson).Should().BeEmpty();
        AssertNoActionAgainstAPerson();
    }

    [Fact]
    public async Task Ending_a_window_early_restores_the_room_at_once()
    {
        await EngageAsync("twitch", LockdownControl.SlowMode, LockdownControl.ShieldMode);
        _time.Advance(TimeSpan.FromMinutes(3));

        Result<LockdownWindowStatus> ended = await RunAsync(s => s.EndAsync(Channel, "twitch"));

        ended.IsSuccess.Should().BeTrue();
        _room.SlowMode.Should().BeFalse();
        _shieldOn.Should().BeFalse();
        LockdownWindowRecord window = Windows().Single();
        window.EndedAt.Should().Be(T0.UtcDateTime.AddMinutes(3));
        window.ExpiresAt.Should().Be(T0.UtcDateTime.AddMinutes(3));
        window.RestoredAt.Should().Be(T0.UtcDateTime.AddMinutes(3));
        ended.Value.RestoredAt.Should().Be(T0.AddMinutes(3));
        AssertNoActionAgainstAPerson();
    }

    [Fact]
    public async Task Ending_when_no_window_is_active_is_not_found()
    {
        Result<LockdownWindowStatus> ended = await RunAsync(s => s.EndAsync(Channel, "twitch"));

        ended.IsFailure.Should().BeTrue();
        ended.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task A_new_engage_after_an_expired_window_restores_the_old_one_first_and_reads_the_true_room()
    {
        await EngageAsync("twitch", LockdownControl.SlowMode);
        _time.Advance(TimeSpan.FromMinutes(20));

        Result<LockdownWindowStatus> again = await EngageAsync("twitch", LockdownControl.SlowMode);

        again.IsSuccess.Should().BeTrue();
        List<LockdownWindowRecord> windows = Windows();
        windows.Should().HaveCount(2);
        windows[0].RestoredAt.Should().NotBeNull();
        windows[1].RestoredAt.Should().BeNull();
        EngagedOf(windows[1])
            .Single()
            .PreviousValue.Should()
            .Be("{\"Enabled\":false,\"Amount\":null}");
        _room.SlowMode.Should().BeTrue();
    }

    [Fact]
    public async Task A_new_engage_is_refused_while_an_old_window_still_cannot_be_restored()
    {
        await EngageAsync("twitch", LockdownControl.ShieldMode);
        _shieldUpdateFails = wanted => !wanted;
        _time.Advance(TimeSpan.FromMinutes(20));

        Result<LockdownWindowStatus> again = await EngageAsync("twitch", LockdownControl.SlowMode);

        again.IsFailure.Should().BeTrue();
        again.ErrorCode.Should().Be("LOCKDOWN_RESTORE_PENDING");
        Windows().Should().ContainSingle();
    }

    [Theory]
    [InlineData("kick")]
    [InlineData("youtube")]
    [InlineData("x")]
    public async Task A_platform_without_an_adapter_engages_nothing_and_lists_every_control_unavailable(
        string platform
    )
    {
        Result<LockdownWindowStatus> result = await EngageAsync(
            platform,
            LockdownControl.FollowersOnly,
            LockdownControl.SlowMode
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Engaged.Should().BeEmpty();
        result
            .Value.Unavailable.Should()
            .Equal(LockdownControl.FollowersOnly, LockdownControl.SlowMode);
        LockdownWindowRecord window = Windows().Should().ContainSingle().Subject;
        window.Platform.Should().Be(platform);
        EngagedOf(window).Should().BeEmpty();
        ControlsOf(window.UnavailableControlsJson)
            .Should()
            .Equal(LockdownControl.FollowersOnly, LockdownControl.SlowMode);
        _chat.ReceivedCalls().Should().BeEmpty();
        _moderation.ReceivedCalls().Should().BeEmpty();

        _time.Advance(TimeSpan.FromMinutes(16));
        (await RestoreDueAsync()).Should().Be(1);
        Windows().Single().RestoredAt.Should().Be(T0.UtcDateTime.AddMinutes(16));
        _chat.ReceivedCalls().Should().BeEmpty();
        _moderation.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_platform_is_a_validation_failure_and_writes_nothing()
    {
        Result<LockdownWindowStatus> result = await EngageAsync(
            "myspace",
            LockdownControl.SlowMode
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        Windows().Should().BeEmpty();
    }

    [Fact]
    public async Task Engage_announces_what_was_tightened_what_was_unavailable_and_what_failed()
    {
        _shieldUpdateFails = wanted => wanted;

        Result<LockdownWindowStatus> result = await EngageAsync(
            "twitch",
            LockdownControl.FollowersOnly,
            LockdownControl.ShieldMode,
            LockdownControl.BlockedTerms
        );

        LockdownEngagedEvent announced = _bus.EventsOf<LockdownEngagedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        announced.BroadcasterId.Should().Be(Channel);
        announced.WindowId.Should().Be(result.Value.Id).And.Be(Windows().Single().Id);
        announced.Platform.Should().Be("twitch");
        announced.Trigger.Should().Be("hate raid: 40 fresh accounts, one phrase");
        announced.StartedAt.Should().Be(T0);
        announced.ExpiresAt.Should().Be(T0.AddMinutes(15));
        announced.OccurredAt.Should().Be(T0);
        announced.Engaged.Should().Equal(LockdownControl.FollowersOnly);
        announced.Unavailable.Should().Equal(LockdownControl.BlockedTerms);
        announced.ApplyFailed.Should().Equal(LockdownControl.ShieldMode);
        _bus.Published.Should().ContainSingle();
    }

    [Fact]
    public async Task A_refused_engage_announces_nothing()
    {
        await EngageAsync("myspace", LockdownControl.SlowMode);

        _bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Engaging_again_announces_the_new_end_time_and_does_not_announce_a_second_engage()
    {
        await EngageAsync("twitch", LockdownControl.SlowMode);
        _time.Advance(TimeSpan.FromMinutes(10));

        await EngageAsync("twitch", LockdownControl.SlowMode);

        LockdownExtendedEvent extended = _bus.EventsOf<LockdownExtendedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        extended.BroadcasterId.Should().Be(Channel);
        extended.WindowId.Should().Be(Windows().Single().Id);
        extended.Platform.Should().Be("twitch");
        extended.ExpiresAt.Should().Be(T0.AddMinutes(25));
        extended.OccurredAt.Should().Be(T0.AddMinutes(10));
        _bus.EventsOf<LockdownEngagedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task Engaging_again_without_a_new_end_time_announces_no_extension()
    {
        _policy = new SpamDefenseSettings { LockdownAutoExtend = false };
        await EngageAsync("twitch", LockdownControl.SlowMode);
        _time.Advance(TimeSpan.FromMinutes(10));

        await EngageAsync("twitch", LockdownControl.SlowMode);

        _bus.EventsOf<LockdownExtendedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task A_full_restore_announces_the_controls_it_put_back()
    {
        await EngageAsync("twitch", LockdownControl.FollowersOnly, LockdownControl.ShieldMode);
        _time.Advance(TimeSpan.FromMinutes(16));

        await RestoreDueAsync();

        LockdownRestoredEvent restored = _bus.EventsOf<LockdownRestoredEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        restored.BroadcasterId.Should().Be(Channel);
        restored.WindowId.Should().Be(Windows().Single().Id);
        restored.Platform.Should().Be("twitch");
        restored.RestoredAt.Should().Be(T0.AddMinutes(16));
        restored.Restored.Should().Equal(LockdownControl.FollowersOnly, LockdownControl.ShieldMode);
        _bus.EventsOf<LockdownRestoreFailedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Ending_a_window_early_announces_the_restore()
    {
        await EngageAsync("twitch", LockdownControl.SlowMode);
        _time.Advance(TimeSpan.FromMinutes(3));

        await RunAsync(s => s.EndAsync(Channel, "twitch"));

        LockdownRestoredEvent restored = _bus.EventsOf<LockdownRestoredEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        restored.RestoredAt.Should().Be(T0.AddMinutes(3));
        restored.Restored.Should().Equal(LockdownControl.SlowMode);
    }

    [Fact]
    public async Task A_failed_restore_announces_the_control_left_tightened_once_and_the_later_success_only_the_retried_one()
    {
        await EngageAsync("twitch", LockdownControl.FollowersOnly, LockdownControl.ShieldMode);
        _shieldUpdateFails = wanted => !wanted;
        _time.Advance(TimeSpan.FromMinutes(16));

        await RestoreDueAsync();

        LockdownRestoreFailedEvent failed = _bus.EventsOf<LockdownRestoreFailedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        failed.BroadcasterId.Should().Be(Channel);
        failed.WindowId.Should().Be(Windows().Single().Id);
        failed.Platform.Should().Be("twitch");
        failed.Failed.Should().Equal(LockdownControl.ShieldMode);
        _bus.EventsOf<LockdownRestoredEvent>().Should().BeEmpty();

        _time.Advance(TimeSpan.FromMinutes(1));
        await RestoreDueAsync();
        _bus.EventsOf<LockdownRestoreFailedEvent>().Should().ContainSingle();

        _shieldUpdateFails = _ => false;
        _time.Advance(TimeSpan.FromMinutes(1));
        await RestoreDueAsync();

        LockdownRestoredEvent restored = _bus.EventsOf<LockdownRestoredEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        restored.Restored.Should().Equal(LockdownControl.ShieldMode);
        restored.RestoredAt.Should().Be(T0.AddMinutes(18));
    }

    [Fact]
    public async Task Active_lists_only_windows_that_are_not_yet_restored_for_this_channel()
    {
        await EngageAsync("twitch", LockdownControl.SlowMode, LockdownControl.ShieldMode);

        IReadOnlyList<LockdownWindowStatus> active = await RunAsync(s => s.GetActiveAsync(Channel));
        IReadOnlyList<LockdownWindowStatus> other = await RunAsync(s =>
            s.GetActiveAsync(Guid.Parse("0199c000-0000-7000-8000-0000000000b2"))
        );

        LockdownWindowStatus window = active.Should().ContainSingle().Subject;
        window.Platform.Should().Be("twitch");
        window.Engaged.Should().Equal(LockdownControl.SlowMode, LockdownControl.ShieldMode);
        window.ExpiresAt.Should().Be(T0.AddMinutes(15));
        window.RestoredAt.Should().BeNull();
        other.Should().BeEmpty();

        _time.Advance(TimeSpan.FromMinutes(16));
        await RestoreDueAsync();
        (await RunAsync(s => s.GetActiveAsync(Channel))).Should().BeEmpty();
    }

    [Fact]
    public async Task Active_still_lists_a_window_whose_restore_left_a_control_tightened()
    {
        await EngageAsync("twitch", LockdownControl.ShieldMode);
        _shieldUpdateFails = wanted => !wanted;
        _time.Advance(TimeSpan.FromMinutes(16));
        await RestoreDueAsync();

        IReadOnlyList<LockdownWindowStatus> active = await RunAsync(s => s.GetActiveAsync(Channel));

        active
            .Should()
            .ContainSingle()
            .Which.RestorationFailed.Should()
            .Equal(LockdownControl.ShieldMode);
    }

    public void Dispose() => _connection.Dispose();
}
