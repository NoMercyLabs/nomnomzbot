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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.AutomationApi.Dtos;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Automation.Entities;
using NomNomzBot.Infrastructure.AutomationApi;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.AutomationApi;

/// <summary>
/// Proves device pairing end to end (stream-deck.md §3/§5): a minted code redeems into a REAL
/// automation token row (hash-only, named after the device, carrying exactly the minted scopes —
/// never <c>chat</c> unless the operator asked at mint time) and hands back the one-time secret;
/// the code is single-use (a second redeem fails and mints nothing); an unknown code mints nothing;
/// a brute-force-guard denial carries the Retry-After hint and neither consumes the code nor mints;
/// and codes are cached with the 5-minute TTL.
/// </summary>
public sealed class AutomationPairingServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f501");
    private static readonly Guid Operator = Guid.Parse("0192a000-0000-7000-8000-00000000f502");
    private static readonly DateTime T0 = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);
    private const string Backend = "https://bot-dev-api.nomercy.tv";

    /// <summary>Dictionary-backed cache that records the TTL each key was stored with.</summary>
    private sealed class FakeCache : ICacheService
    {
        private readonly Dictionary<string, object> _store = [];
        public Dictionary<string, TimeSpan?> Ttls { get; } = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.TryGetValue(key, out object? value) ? (T?)value : default);

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? expiry = null,
            CancellationToken ct = default
        )
        {
            _store[key] = value!;
            Ttls[key] = expiry;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_store.ContainsKey(key));
    }

    private sealed class Harness
    {
        public required AutomationPairingService Service { get; init; }
        public required AutomationTestDbContext Db { get; init; }
        public required FakeCache Cache { get; init; }
        public required IRateLimiterPartitionStore Limiter { get; init; }
    }

    private static Harness Build(
        bool rateLimited = false,
        IEnumerable<ICommandAction>? actions = null
    )
    {
        AutomationTestDbContext db = AutomationTestDbContext.New();
        FakeCache cache = new();
        FakeTimeProvider clock = new(new(T0));

        AutomationApiTokenService tokens = new(
            db,
            new RecordingEventBus(),
            clock,
            new Infrastructure.AutomationApi.Events.AutomationEventRegistry([])
        );

        IRateLimiterPartitionStore limiter = Substitute.For<IRateLimiterPartitionStore>();
        limiter
            .AcquireAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                rateLimited
                    ? new(false, 0, TimeSpan.FromSeconds(42))
                    : new RateLimitLease(true, 4, TimeSpan.Zero)
            );

        return new()
        {
            Service = new(cache, tokens, limiter, db, actions ?? [], clock),
            Db = db,
            Cache = cache,
            Limiter = limiter,
        };
    }

    private static DeviceInfo Deck(string? name = "Office Deck") =>
        new() { Kind = "Stream Deck", Name = name };

    private static DeviceInfo StreamDeckPlugin(string? name = "Office Deck") =>
        new() { Kind = "streamdeck", Name = name };

    private static ICommandAction FakeAction(string type)
    {
        ICommandAction action = Substitute.For<ICommandAction>();
        action.ActionType.Returns(type);
        return action;
    }

    [Fact]
    public async Task A_minted_code_redeems_into_a_real_scoped_token_exactly_once()
    {
        Harness h = Build();

        Result<PairingCodeDto> minted = await h.Service.MintCodeAsync(
            Channel,
            Operator,
            new() { DeviceLabel = "Studio Deck" }
        );
        minted.IsSuccess.Should().BeTrue(minted.ErrorMessage);
        minted.Value.Code.Should().HaveLength(8);
        minted.Value.ExpiresAt.Should().Be(T0.AddMinutes(5));
        h.Cache.Ttls[$"pair:{minted.Value.Code}"].Should().Be(TimeSpan.FromMinutes(5));

        Result<PairingRedemptionDto> redeemed = await h.Service.RedeemCodeAsync(
            minted.Value.Code,
            Deck(),
            "203.0.113.7",
            Backend
        );

        redeemed.IsSuccess.Should().BeTrue(redeemed.ErrorMessage);
        redeemed.Value.BackendUrl.Should().Be(Backend);
        redeemed.Value.Token.Should().StartWith("nnzb_ak_");
        redeemed
            .Value.Scopes.Should()
            .BeEquivalentTo(["invoke", "events", "read"], "the safe default excludes chat");

        // The credential is a REAL token row: hash-only, named after the device, exact scopes.
        AutomationApiToken row = await h.Db.AutomationApiTokens.SingleAsync();
        row.BroadcasterId.Should().Be(Channel);
        row.CreatedByUserId.Should().Be(Operator);
        row.Name.Should().Be("Stream Deck: Office Deck");
        row.TokenHash.Should().Be(AutomationApiTokenService.HashSecret(redeemed.Value.Token));
        row.ScopesJson.Should().NotContain("chat");

        // Single-use: the same code never yields a second credential.
        Result<PairingRedemptionDto> replay = await h.Service.RedeemCodeAsync(
            minted.Value.Code,
            Deck(),
            "203.0.113.7",
            Backend
        );
        replay.IsFailure.Should().BeTrue();
        replay.ErrorCode.Should().Be("UNAUTHENTICATED");
        (await h.Db.AutomationApiTokens.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Chat_is_granted_only_when_the_operator_asked_at_mint_time()
    {
        Harness h = Build();
        Result<PairingCodeDto> minted = await h.Service.MintCodeAsync(
            Channel,
            Operator,
            new() { DeviceLabel = "Chatty Deck", Scopes = ["invoke", "chat"] }
        );

        Result<PairingRedemptionDto> redeemed = await h.Service.RedeemCodeAsync(
            minted.Value.Code,
            Deck("Chatty"),
            "203.0.113.7",
            Backend
        );

        redeemed.Value.Scopes.Should().BeEquivalentTo(["invoke", "chat"]);
        (await h.Db.AutomationApiTokens.SingleAsync()).ScopesJson.Should().Contain("chat");
    }

    [Fact]
    public async Task An_unknown_code_mints_nothing()
    {
        Harness h = Build();

        Result<PairingRedemptionDto> result = await h.Service.RedeemCodeAsync(
            "WRONGCOD",
            Deck(),
            "203.0.113.7",
            Backend
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("UNAUTHENTICATED");
        (await h.Db.AutomationApiTokens.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_guard_denial_carries_RetryAfter_and_consumes_nothing()
    {
        Harness h = Build(rateLimited: true);
        Result<PairingCodeDto> minted = await h.Service.MintCodeAsync(
            Channel,
            Operator,
            new() { DeviceLabel = "Deck" }
        );

        Result<PairingRedemptionDto> denied = await h.Service.RedeemCodeAsync(
            minted.Value.Code,
            Deck(),
            "203.0.113.7",
            Backend
        );

        denied.IsFailure.Should().BeTrue();
        denied.ErrorCode.Should().Be("RATE_LIMITED");
        denied.ErrorDetail.Should().Be("42", "the Retry-After seconds ride the error detail");
        (await h.Db.AutomationApiTokens.CountAsync()).Should().Be(0);
        (await h.Cache.ExistsAsync($"pair:{minted.Value.Code}"))
            .Should()
            .BeTrue("a denied attempt must not burn the code");
    }

    [Fact]
    public async Task Mint_rejects_an_unknown_scope_and_a_blank_label()
    {
        Harness h = Build();

        Result<PairingCodeDto> badScope = await h.Service.MintCodeAsync(
            Channel,
            Operator,
            new() { DeviceLabel = "Deck", Scopes = ["admin"] }
        );
        badScope.IsFailure.Should().BeTrue();
        badScope.ErrorCode.Should().Be("VALIDATION_FAILED");

        Result<PairingCodeDto> blankLabel = await h.Service.MintCodeAsync(
            Channel,
            Operator,
            new() { DeviceLabel = "  " }
        );
        blankLabel.IsFailure.Should().BeTrue();
        h.Cache.Ttls.Should().BeEmpty("nothing was cached for rejected mints");
    }

    [Fact]
    public async Task Pairing_a_stream_deck_auto_provisions_one_pipeline_per_music_action()
    {
        Harness h = Build(
            actions: [FakeAction("music_play"), FakeAction("music_pause"), FakeAction("chat_send")]
        );
        Result<PairingCodeDto> minted = await h.Service.MintCodeAsync(
            Channel,
            Operator,
            new() { DeviceLabel = "Studio Deck" }
        );

        await h.Service.RedeemCodeAsync(
            minted.Value.Code,
            StreamDeckPlugin(),
            "203.0.113.7",
            Backend
        );

        List<Domain.Commands.Entities.Pipeline> pipelines = await h
            .Db.Pipelines.Where(p => p.BroadcasterId == Channel)
            .ToListAsync();
        pipelines.Should().HaveCount(2, "only the music_* actions get a pipeline, not chat_send");
        pipelines.Select(p => p.Name).Should().BeEquivalentTo(["music_play", "music_pause"]);
        pipelines.Should().OnlyContain(p => p.GraphJsonCache!.Contains("\"type\":\"music_"));
    }

    [Fact]
    public async Task Pairing_a_non_stream_deck_device_provisions_nothing()
    {
        Harness h = Build(actions: [FakeAction("music_play")]);
        Result<PairingCodeDto> minted = await h.Service.MintCodeAsync(
            Channel,
            Operator,
            new() { DeviceLabel = "Touch Portal" }
        );

        await h.Service.RedeemCodeAsync(minted.Value.Code, Deck(), "203.0.113.7", Backend);

        (await h.Db.Pipelines.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Re_pairing_a_stream_deck_does_not_duplicate_pipelines()
    {
        Harness h = Build(actions: [FakeAction("music_play")]);
        Result<PairingCodeDto> first = await h.Service.MintCodeAsync(
            Channel,
            Operator,
            new() { DeviceLabel = "Studio Deck" }
        );
        await h.Service.RedeemCodeAsync(
            first.Value.Code,
            StreamDeckPlugin(),
            "203.0.113.7",
            Backend
        );

        Result<PairingCodeDto> second = await h.Service.MintCodeAsync(
            Channel,
            Operator,
            new() { DeviceLabel = "Second Deck" }
        );
        await h.Service.RedeemCodeAsync(
            second.Value.Code,
            StreamDeckPlugin("Second Deck"),
            "203.0.113.8",
            Backend
        );

        (await h.Db.Pipelines.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Device_init_then_approve_then_poll_yields_a_real_scoped_token()
    {
        Harness h = Build();

        Result<DeviceInitDto> init = await h.Service.InitDeviceAsync(
            StreamDeckPlugin(),
            Backend,
            scopes: null,
            CancellationToken.None
        );
        init.IsSuccess.Should().BeTrue(init.ErrorMessage);
        init.Value.UserCode.Should().HaveLength(8);
        init.Value.VerificationUri.Should()
            .Be($"{Backend}/api/v1/automation/pair/device/approve?code={init.Value.UserCode}");

        // Not approved yet — poll must stay pending and mint nothing.
        Result<DevicePollDto> pendingPoll = await h.Service.PollDeviceAsync(init.Value.DeviceCode);
        pendingPoll.IsSuccess.Should().BeTrue();
        pendingPoll.Value.Status.Should().Be("pending");
        pendingPoll.Value.Token.Should().BeNull();

        Result approved = await h.Service.ApproveDeviceAsync(
            Channel,
            Operator,
            init.Value.UserCode
        );
        approved.IsSuccess.Should().BeTrue(approved.ErrorMessage);

        Result<DevicePollDto> approvedPoll = await h.Service.PollDeviceAsync(init.Value.DeviceCode);
        approvedPoll.IsSuccess.Should().BeTrue(approvedPoll.ErrorMessage);
        approvedPoll.Value.Status.Should().Be("approved");
        approvedPoll.Value.Token.Should().NotBeNullOrEmpty();
        approvedPoll.Value.BackendUrl.Should().Be(Backend);

        AutomationApiToken row = await h.Db.AutomationApiTokens.SingleAsync();
        row.BroadcasterId.Should().Be(Channel);
        row.CreatedByUserId.Should().Be(Operator);
        row.TokenHash.Should().Be(AutomationApiTokenService.HashSecret(approvedPoll.Value.Token!));

        // The mint is one-shot: polling again after a successful mint finds nothing left to give out.
        Result<DevicePollDto> replay = await h.Service.PollDeviceAsync(init.Value.DeviceCode);
        replay.IsFailure.Should().BeTrue();
        replay.ErrorCode.Should().Be("NOT_FOUND");
        (await h.Db.AutomationApiTokens.CountAsync())
            .Should()
            .Be(1, "the replayed poll must not mint a second token");
    }

    [Fact]
    public async Task Approving_a_stream_deck_device_auto_provisions_pipelines_same_as_the_code_flow()
    {
        Harness h = Build(actions: [FakeAction("music_play")]);
        Result<DeviceInitDto> init = await h.Service.InitDeviceAsync(
            StreamDeckPlugin(),
            Backend,
            scopes: null,
            CancellationToken.None
        );
        await h.Service.ApproveDeviceAsync(Channel, Operator, init.Value.UserCode);
        await h.Service.PollDeviceAsync(init.Value.DeviceCode);

        (await h.Db.Pipelines.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task An_unknown_or_expired_user_code_is_rejected_by_approve()
    {
        Harness h = Build();

        Result approved = await h.Service.ApproveDeviceAsync(Channel, Operator, "WRONGCOD");

        approved.IsFailure.Should().BeTrue();
        approved.ErrorCode.Should().Be("UNAUTHENTICATED");
    }

    [Fact]
    public async Task A_user_code_can_only_ever_be_approved_once()
    {
        Harness h = Build();
        Result<DeviceInitDto> init = await h.Service.InitDeviceAsync(
            StreamDeckPlugin(),
            Backend,
            scopes: null,
            CancellationToken.None
        );

        Result first = await h.Service.ApproveDeviceAsync(Channel, Operator, init.Value.UserCode);
        first.IsSuccess.Should().BeTrue();

        Result second = await h.Service.ApproveDeviceAsync(Channel, Operator, init.Value.UserCode);
        second.IsFailure.Should().BeTrue("the user-code index is consumed on first approval");
    }

    [Fact]
    public async Task Polling_an_unknown_device_code_fails_without_minting()
    {
        Harness h = Build();

        Result<DevicePollDto> poll = await h.Service.PollDeviceAsync("not-a-real-device-code");

        poll.IsFailure.Should().BeTrue();
        poll.ErrorCode.Should().Be("NOT_FOUND");
        (await h.Db.AutomationApiTokens.CountAsync()).Should().Be(0);
    }
}
