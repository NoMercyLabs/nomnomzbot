// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.BackgroundServices;
using NomNomzBot.Infrastructure.Tests.Content;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.BackgroundServices;

/// <summary>
/// After a deploy the incoming instance re-subscribes every channel. One channel at a time left the last one deaf
/// for about two minutes; these tests pin the wave: channels reconcile side by side, bounded, live ones first.
/// </summary>
public sealed class BotLifecycleServiceSyncTests
{
    private const int ChannelCount = 20;

    [Fact]
    public async Task A_deploy_resubscribes_a_full_wave_of_channels_at_once_with_live_channels_in_it()
    {
        string database = Guid.NewGuid().ToString();
        // Live channels sort LAST by name and are inserted last, so neither name nor insertion order puts
        // them in the first wave — only live-first ordering does.
        List<Guid> live = [SeedChannel(database, "zz-live-a", isLive: true)];
        List<Guid> offline = [];
        for (int i = 0; i < ChannelCount - 2; i++)
            offline.Add(SeedChannel(database, $"offline-{i:D2}", isLive: false));
        live.Add(SeedChannel(database, "zz-live-b", isLive: true));

        ConcurrentQueue<Guid> started = new();
        TaskCompletionSource firstWaveStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        ITwitchEventSubService eventSub = Substitute.For<ITwitchEventSubService>();
        eventSub
            .EnsureSubscribedAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(async call =>
            {
                Guid channelId = call.ArgAt<Guid>(0);
                if (channelId == Guid.Empty)
                    return Result.Success();

                started.Enqueue(channelId);
                if (started.Count == BotLifecycleService.MaxConcurrentChannelSyncs)
                    firstWaveStarted.TrySetResult();
                await release.Task;
                return Result.Success();
            });

        BotLifecycleService service = Build(database, eventSub);

        Task sync = service.SyncChannelsAsync(CancellationToken.None, reconcileStale: false);
        await firstWaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The whole first wave is in flight together — and not one channel more than the bound.
        Guid[] firstWave = [.. started];
        firstWave.Should().HaveCount(BotLifecycleService.MaxConcurrentChannelSyncs);
        firstWave.Should().Contain(live, "live channels are resubscribed before offline ones");

        release.SetResult();
        await sync.WaitAsync(TimeSpan.FromSeconds(10));

        started
            .Should()
            .BeEquivalentTo(
                [.. live, .. offline],
                "every active channel is reconciled exactly once per sweep"
            );
    }

    [Fact]
    public async Task One_channel_failing_to_subscribe_does_not_stop_the_others()
    {
        string database = Guid.NewGuid().ToString();
        Guid broken = SeedChannel(database, "broken", isLive: true);
        Guid healthy = SeedChannel(database, "healthy", isLive: false);

        ConcurrentBag<Guid> subscribed = [];
        ITwitchEventSubService eventSub = Substitute.For<ITwitchEventSubService>();
        eventSub
            .EnsureSubscribedAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                Guid channelId = call.ArgAt<Guid>(0);
                if (channelId == broken)
                    throw new InvalidOperationException("Twitch went away");
                subscribed.Add(channelId);
                return Task.FromResult(Result.Success());
            });

        await Build(database, eventSub)
            .SyncChannelsAsync(CancellationToken.None, reconcileStale: false);

        subscribed.Should().Contain(healthy);
    }

    private static Guid SeedChannel(string database, string name, bool isLive)
    {
        Guid id = Guid.NewGuid();
        using SeedTestDbContext db = SeedTestDbContext.New(database);
        db.Channels.Add(
            new Channel
            {
                Id = id,
                OwnerUserId = id,
                Name = name,
                NameNormalized = name,
                ExternalChannelId = id.ToString(),
                Enabled = true,
                IsOnboarded = true,
                Status = AuthEnums.ChannelStatus.Active,
                IsLive = isLive,
            }
        );
        db.SaveChanges();
        return id;
    }

    private static BotLifecycleService Build(string database, ITwitchEventSubService eventSub)
    {
        ITwitchStreamsApi streams = Substitute.For<ITwitchStreamsApi>();
        streams
            .GetStreamAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TwitchStream>("offline", TwitchErrorCodes.NotFound));

        ServiceCollection services = new();
        services.AddScoped<IApplicationDbContext>(_ => SeedTestDbContext.New(database));
        services.AddSingleton(eventSub);
        services.AddSingleton(streams);

        return new BotLifecycleService(
            services.BuildServiceProvider(),
            NullLogger<BotLifecycleService>.Instance
        );
    }
}
