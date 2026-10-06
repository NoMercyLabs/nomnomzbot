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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Platform.Security;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// The conduit coordinator (twitch-eventsub.md §10): first boot creates and persists ONE two-shard conduit,
/// a restart reuses it without a create, a foreign conduit on the same client id is never adopted, a shard
/// left disabled by a crash is reclaimed, and the stopping instance only reports a successor once Twitch
/// shows another shard enabled. Twitch is modelled in memory and shared between the "instances".
/// </summary>
public sealed class EventSubConduitShardCoordinatorTests : IDisposable
{
    private readonly EventSubTestDbContext _db = EventSubTestDbContext.New();
    private readonly FakeTwitchConduits _twitch = new();

    public void Dispose() => _db.Dispose();

    private EventSubConduitShardCoordinator NewInstance()
    {
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(_db)
            .AddSingleton<ITwitchEventSubConduitsApi>(_twitch)
            .AddSingleton<IRunOnceGuard>(new AlwaysGrantedGuard())
            .BuildServiceProvider();
        return new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            new OutboundSanctionAccessor(),
            NullLogger<EventSubConduitShardCoordinator>.Instance
        );
    }

    [Fact]
    public async Task First_boot_creates_a_two_shard_conduit_and_persists_it()
    {
        Result<string> ensured = await NewInstance().EnsureConduitAsync();

        ensured.IsSuccess.Should().BeTrue();
        _twitch.Calls.Should().Contain("POST conduits 2");
        EventSubConduit row = await _db.EventSubConduits.SingleAsync();
        row.ConduitId.Should().Be(ensured.Value);
        row.ShardCount.Should().Be(2);
        row.Status.Should().Be("active");
    }

    [Fact]
    public async Task A_restart_reuses_the_persisted_conduit_without_creating_one()
    {
        string first = (await NewInstance().EnsureConduitAsync()).Value;
        _twitch.Calls.Clear();

        Result<string> second = await NewInstance().EnsureConduitAsync();

        second.Value.Should().Be(first);
        _twitch.Calls.Should().NotContain(c => c.StartsWith("POST conduits"));
        _twitch.ConduitIds.Should().ContainSingle();
        (await _db.EventSubConduits.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_conduit_this_deployment_did_not_persist_is_never_adopted()
    {
        // Another deployment on the same client id (local dev next to the dev server) owns this one.
        _twitch.Seed("foreign", 2);

        Result<string> ensured = await NewInstance().EnsureConduitAsync();

        ensured.Value.Should().NotBe("foreign");
        _twitch.Calls.Should().Contain("POST conduits 2");
    }

    [Fact]
    public async Task A_conduit_Twitch_no_longer_has_is_replaced_in_the_same_row()
    {
        string first = (await NewInstance().EnsureConduitAsync()).Value;
        _twitch.DropOnTwitch(first); // e.g. deleted after 72 h with no enabled shard

        Result<string> replaced = await NewInstance().EnsureConduitAsync();

        replaced.Value.Should().NotBe(first);
        EventSubConduit row = await _db.EventSubConduits.SingleAsync();
        row.ConduitId.Should().Be(replaced.Value);
    }

    [Fact]
    public async Task A_drifted_shard_count_is_corrected_to_two()
    {
        string conduit = (await NewInstance().EnsureConduitAsync()).Value;
        _twitch.ResizeOnTwitch(conduit, 5);
        _twitch.Calls.Clear();

        await NewInstance().EnsureConduitAsync();

        _twitch.Calls.Should().Contain($"PATCH conduits {conduit} 2");
    }

    [Fact]
    public async Task No_app_token_fails_so_the_caller_stays_on_per_owner_sessions()
    {
        _twitch.FailWith = TwitchErrorCodes.NoToken;

        Result<string> ensured = await NewInstance().EnsureConduitAsync();

        ensured.ErrorCode.Should().Be(TwitchErrorCodes.NoToken);
        (await _db.EventSubConduits.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_fresh_conduit_whose_shards_Twitch_does_not_list_yet_is_still_claimed()
    {
        // Production 2026-09-29: a brand-new conduit listed no usable shard, every claim failed with
        // "every shard is enabled on another session", and Twitch closed each idle shard socket (4003).
        _twitch.ListsOnlyBoundShards = true;
        EventSubConduitShardCoordinator blue = NewInstance();
        EventSubConduitShardCoordinator green = NewInstance();

        Result<string> blueShard = await blue.ClaimShardAsync("session-blue");
        Result<string> greenShard = await green.ClaimShardAsync("session-green");

        blueShard.IsSuccess.Should().BeTrue();
        greenShard.IsSuccess.Should().BeTrue();
        blueShard.Value.Should().NotBe(greenShard.Value);
        _twitch.Shard(blue.ConduitId!, blueShard.Value).Should().Be(("enabled", "session-blue"));
        _twitch.Shard(blue.ConduitId!, greenShard.Value).Should().Be(("enabled", "session-green"));
    }

    [Fact]
    public async Task Two_instances_each_bind_a_different_shard_to_their_own_session()
    {
        EventSubConduitShardCoordinator blue = NewInstance();
        EventSubConduitShardCoordinator green = NewInstance();

        Result<string> blueShard = await blue.ClaimShardAsync("session-blue");
        Result<string> greenShard = await green.ClaimShardAsync("session-green");

        blueShard.Value.Should().NotBe(greenShard.Value);
        string conduit = blue.ConduitId!;
        _twitch.Shard(conduit, blueShard.Value).Should().Be(("enabled", "session-blue"));
        _twitch.Shard(conduit, greenShard.Value).Should().Be(("enabled", "session-green"));

        // The claim is recorded for the admin view.
        List<EventSubConduitShard> rows = await _db.EventSubConduitShards.ToListAsync();
        rows.Select(r => (r.ShardId, r.SessionId, r.Status))
            .Should()
            .BeEquivalentTo([
                (blueShard.Value, "session-blue", "enabled"),
                (greenShard.Value, "session-green", "enabled"),
            ]);
    }

    [Fact]
    public async Task A_third_session_gets_no_shard_while_both_are_enabled()
    {
        await NewInstance().ClaimShardAsync("session-blue");
        await NewInstance().ClaimShardAsync("session-green");

        Result<string> third = await NewInstance().ClaimShardAsync("session-third");

        third.IsFailure.Should().BeTrue();
        _twitch.Calls.Should().NotContain(c => c.Contains("->session-third"));
    }

    [Fact]
    public async Task A_restart_after_a_crash_reclaims_the_shard_the_dead_session_left_disabled()
    {
        EventSubConduitShardCoordinator crashed = NewInstance();
        string crashedShard = (await crashed.ClaimShardAsync("session-dead")).Value;
        EventSubConduitShardCoordinator survivor = NewInstance();
        await survivor.ClaimShardAsync("session-alive");

        _twitch.Disconnect("session-dead");
        Result<string> reclaimed = await NewInstance().ClaimShardAsync("session-restarted");

        reclaimed.Value.Should().Be(crashedShard);
        _twitch
            .Shard(crashed.ConduitId!, crashedShard)
            .Should()
            .Be(("enabled", "session-restarted"));
    }

    [Fact]
    public async Task The_stopping_instance_sees_its_successor_only_once_the_successor_shard_is_enabled()
    {
        EventSubConduitShardCoordinator outgoing = NewInstance();
        await outgoing.ClaimShardAsync("session-old");

        // No successor yet: the wait times out and reports none.
        (await outgoing.WaitForSuccessorShardAsync(TimeSpan.FromMilliseconds(700)))
            .Should()
            .BeFalse();

        await NewInstance().ClaimShardAsync("session-new");

        (await outgoing.WaitForSuccessorShardAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
    }

    [Fact]
    public async Task A_session_reconnect_rebinds_the_shard_this_instance_already_holds()
    {
        EventSubConduitShardCoordinator instance = NewInstance();
        string shard = (await instance.ClaimShardAsync("session-1")).Value;

        // Twitch's session_reconnect: the shard is still enabled on the old session id.
        Result<string> rebound = await instance.ClaimShardAsync("session-2");

        rebound.Value.Should().Be(shard);
        _twitch.Shard(instance.ConduitId!, shard).Should().Be(("enabled", "session-2"));
    }

    [Fact]
    public async Task A_shard_left_disconnected_by_a_departed_session_is_adopted_onto_the_survivor_session()
    {
        // Incident 2026-10-05: after the switch the old colour's shard stayed websocket_disconnected, nobody
        // claimed it, and Twitch dropped every notification routed to it for three hours.
        EventSubConduitShardCoordinator survivor = NewInstance();
        string survivorShard = (await survivor.ClaimShardAsync("session-survivor")).Value;
        string departedShard = (await NewInstance().ClaimShardAsync("session-departed")).Value;
        string conduit = survivor.ConduitId!;
        _twitch.Disconnect("session-departed");
        _twitch.Calls.Clear();

        Result<IReadOnlyList<string>> adopted = await survivor.AdoptOrphanedShardsAsync();

        adopted.Value.Should().Equal(departedShard);
        _twitch.Calls.Should().Contain($"PATCH shards {conduit} {departedShard}->session-survivor");
        _twitch.Shard(conduit, departedShard).Should().Be(("enabled", "session-survivor"));
        _twitch.Shard(conduit, survivorShard).Should().Be(("enabled", "session-survivor"));
    }

    [Fact]
    public async Task Nothing_is_adopted_while_every_shard_is_enabled()
    {
        EventSubConduitShardCoordinator blue = NewInstance();
        await blue.ClaimShardAsync("session-blue");
        await NewInstance().ClaimShardAsync("session-green");
        _twitch.Calls.Clear();

        Result<IReadOnlyList<string>> adopted = await blue.AdoptOrphanedShardsAsync();

        adopted.Value.Should().BeEmpty();
        _twitch.Calls.Should().NotContain(c => c.StartsWith("PATCH shards"));
    }

    [Fact]
    public async Task An_instance_that_released_its_claim_adopts_nothing()
    {
        // The outgoing colour releases its claim once its session is closed; a late re-check must not pull
        // a shard onto that dead session.
        EventSubConduitShardCoordinator outgoing = NewInstance();
        await outgoing.ClaimShardAsync("session-old");
        outgoing.ReleaseClaim();
        _twitch.Disconnect("session-old");
        _twitch.Calls.Clear();

        Result<IReadOnlyList<string>> adopted = await outgoing.AdoptOrphanedShardsAsync();

        adopted.Value.Should().BeEmpty();
        _twitch.Calls.Should().NotContain(c => c.StartsWith("PATCH shards"));
    }

    [Fact]
    public async Task An_incoming_session_takes_a_shard_the_active_session_adopted()
    {
        // The active colour holds both shards on one session; the incoming colour must still get one, or
        // Twitch closes its idle socket and the handover has no successor.
        EventSubConduitShardCoordinator blue = NewInstance();
        string blueShard = (await blue.ClaimShardAsync("session-blue")).Value;
        await blue.AdoptOrphanedShardsAsync();
        _twitch.Shard(blue.ConduitId!, "1").Should().Be(("enabled", "session-blue"));

        Result<string> greenShard = await NewInstance().ClaimShardAsync("session-green");

        greenShard.IsSuccess.Should().BeTrue();
        greenShard.Value.Should().NotBe(blueShard);
        _twitch.Shard(blue.ConduitId!, greenShard.Value).Should().Be(("enabled", "session-green"));
        _twitch.Shard(blue.ConduitId!, blueShard).Should().Be(("enabled", "session-blue"));
    }

    [Fact]
    public async Task A_shard_adopted_onto_our_own_session_is_not_taken_for_a_successor()
    {
        EventSubConduitShardCoordinator outgoing = NewInstance();
        await outgoing.ClaimShardAsync("session-old");
        await outgoing.AdoptOrphanedShardsAsync();
        _twitch.EnabledShardCount(outgoing.ConduitId!).Should().Be(2);

        (await outgoing.WaitForSuccessorShardAsync(TimeSpan.FromMilliseconds(700)))
            .Should()
            .BeFalse();
    }

    private sealed class AlwaysGrantedGuard : IRunOnceGuard
    {
        public Task<IAsyncDisposable?> TryAcquireAsync(
            string resourceName,
            TimeSpan ttl,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IAsyncDisposable?>(new Released());

        private sealed class Released : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
