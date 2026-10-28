// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Domain.Moderation.ChatSafety;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Moderation.ChatSafety;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// The <c>chat_safety_status</c> push: at once on a level change, and at most once every 2 seconds while
/// a meter moves.
/// </summary>
public class ChatSafetyStatusBroadcasterTests
{
    private static readonly Guid Channel = Guid.Parse("0199c000-0000-7000-8000-0000000000b1");

    private readonly FakeTimeProvider _time = new(
        new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero)
    );
    private readonly IDashboardNotifier _notifier = Substitute.For<IDashboardNotifier>();
    private readonly ChatSafetyMeter _meter;
    private readonly ChatSafetyStatusBroadcaster _broadcaster;

    public ChatSafetyStatusBroadcasterTests()
    {
        _meter = new ChatSafetyMeter(_time);
        ServiceProvider services = new ServiceCollection()
            .AddSingleton(_notifier)
            .BuildServiceProvider();
        _broadcaster = new ChatSafetyStatusBroadcaster(
            _meter,
            services.GetRequiredService<IServiceScopeFactory>(),
            _time,
            NullLogger<ChatSafetyStatusBroadcaster>.Instance
        );
    }

    private List<ChatSafetyStatusDto> Pushed() =>
        [
            .. _notifier
                .ReceivedCalls()
                .Where(c => c.GetMethodInfo().Name == nameof(IDashboardNotifier.NotifyChannelAsync))
                .Where(c => (string)c.GetArguments()[1]! == ChatSafetyStatusBroadcaster.HubMethod)
                .Select(c => (ChatSafetyStatusDto)c.GetArguments()[2]!),
        ];

    [Fact]
    public async Task A_quiet_channel_pushes_nothing()
    {
        _meter.Snapshot(Channel);

        await _broadcaster.TickAsync(CancellationToken.None);

        Pushed().Should().BeEmpty();
    }

    [Fact]
    public async Task A_moving_meter_pushes_at_most_once_every_two_seconds()
    {
        _meter.RecordFollow(Channel);
        await _broadcaster.TickAsync(CancellationToken.None);
        Pushed().Should().ContainSingle().Which.FollowsPerMinute.Should().Be(1);

        _meter.RecordFollow(Channel);
        _time.Advance(TimeSpan.FromSeconds(1));
        await _broadcaster.TickAsync(CancellationToken.None);
        Pushed().Should().HaveCount(1);

        _time.Advance(TimeSpan.FromSeconds(1));
        await _broadcaster.TickAsync(CancellationToken.None);
        Pushed().Select(p => p.FollowsPerMinute).Should().Equal(1, 2);
    }

    [Fact]
    public async Task A_level_change_pushes_at_once_and_Over_follows_Raid()
    {
        _meter.ObserveCohort(
            Channel,
            "buy followers",
            CohortVerdict.Campaign,
            8,
            _time.GetUtcNow().AddSeconds(30)
        );
        await _broadcaster.TickAsync(CancellationToken.None);
        Pushed().Select(p => p.Level).Should().Equal(ChatSafetyLevel.Raid);

        _time.Advance(TimeSpan.FromSeconds(31));
        await _broadcaster.TickAsync(CancellationToken.None);
        Pushed().Select(p => p.Level).Should().Equal(ChatSafetyLevel.Raid, ChatSafetyLevel.Over);

        _time.Advance(TimeSpan.FromMinutes(5));
        await _broadcaster.TickAsync(CancellationToken.None);
        Pushed()
            .Select(p => p.Level)
            .Should()
            .Equal(ChatSafetyLevel.Raid, ChatSafetyLevel.Over, ChatSafetyLevel.Calm);
    }
}
