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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Chat.Kick;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat.Kick;

/// <summary>
/// Proves the Kick chat-READ reconcile semantics: only a TENANT-SCOPED kick connection (the deliberate
/// connect opt-in) drives work — an identity-plane login connection never does; the streamer's Kick
/// presence is provisioned as its own tenant BEFORE the subscription (so a first webhook always
/// resolves); an already-subscribed channel is adopted with no duplicate create; and a missing
/// <c>events:subscribe</c> grant backs the tenant off instead of hammering guaranteed 403s every tick.
/// </summary>
public sealed class KickEventSubscriptionWorkerTests
{
    private static readonly Guid PrimaryChannel = Guid.Parse(
        "0192e000-0000-7000-8000-0000000000a1"
    );
    private static readonly Guid KickTenant = Guid.Parse("0192e000-0000-7000-8000-0000000000a2");
    private static readonly Guid Owner = Guid.Parse("0192e000-0000-7000-8000-0000000000a9");

    /// <summary>Kick's full verified webhook event surface — what the reconcile must keep subscribed.</summary>
    private static readonly string[] WantedEventNames =
    [
        "chat.message.sent",
        "channel.followed",
        "channel.subscription.new",
        "channel.subscription.renewal",
        "channel.subscription.gifts",
        "channel.reward.redemption.updated",
        "livestream.status.updated",
        "livestream.metadata.updated",
        "moderation.banned",
        "kicks.gifted",
    ];

    private static (
        KickEventSubscriptionWorker Worker,
        AuthDbContext Db,
        IPlatformChannelProvisioner Provisioner,
        IKickApiClient Client
    ) Build(
        IReadOnlyList<KickEventSubscription>? existing = null,
        Result? subscribeOutcome = null,
        Result<IReadOnlyList<KickEventSubscription>>? listOutcome = null
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new Channel
            {
                Id = PrimaryChannel,
                OwnerUserId = Owner,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "tw-1",
                TwitchChannelId = "tw-1",
                Name = "streamer",
                NameNormalized = "streamer",
                IsOnboarded = true,
                DeploymentMode = AuthEnums.DeploymentMode.Saas,
                BillingTierKey = "free",
            }
        );
        db.SaveChanges();

        IPlatformChannelProvisioner provisioner = Substitute.For<IPlatformChannelProvisioner>();
        provisioner
            .GetOrCreateAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(KickTenant);

        IKickAccessTokenProvider tokens = Substitute.For<IKickAccessTokenProvider>();
        tokens
            .GetAsync(KickTenant, Arg.Any<CancellationToken>())
            .Returns(new KickAccess("kick-bearer-1", 12345));

        IKickApiClient client = Substitute.For<IKickApiClient>();
        client
            .ListEventSubscriptionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                listOutcome ?? Result.Success<IReadOnlyList<KickEventSubscription>>(existing ?? [])
            );
        client
            .SubscribeAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<KickEventRequest>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(subscribeOutcome ?? Result.Success());

        ServiceProvider provider = new ServiceCollection()
            .AddSingleton<IApplicationDbContext>(db)
            .AddScoped<IPlatformChannelProvisioner>(_ => provisioner)
            .AddScoped<IKickAccessTokenProvider>(_ => tokens)
            .BuildServiceProvider();

        KickEventSubscriptionWorker worker = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            client,
            TimeProvider.System,
            NullLogger<KickEventSubscriptionWorker>.Instance
        );
        return (worker, db, provisioner, client);
    }

    private static void SeedConnection(AuthDbContext db, Guid? broadcasterId)
    {
        db.IntegrationConnections.Add(
            new IntegrationConnection
            {
                BroadcasterId = broadcasterId,
                Provider = AuthEnums.IntegrationProvider.Kick,
                ProviderAccountId = "12345",
                ProviderAccountName = "StreamerGal",
                Status = "connected",
                Scopes = ["chat:write", "events:subscribe"],
            }
        );
        db.SaveChanges();
    }

    [Fact]
    public async Task A_connected_streamer_gets_provisioned_and_subscribed()
    {
        (
            KickEventSubscriptionWorker worker,
            AuthDbContext db,
            IPlatformChannelProvisioner provisioner,
            IKickApiClient client
        ) = Build();
        SeedConnection(db, PrimaryChannel);

        await worker.TickAsync(CancellationToken.None);

        // The Kick presence becomes its own tenant BEFORE the subscription, keyed by the numeric id.
        await provisioner
            .Received(1)
            .GetOrCreateAsync(
                Owner,
                AuthEnums.Platform.Kick,
                "12345",
                "StreamerGal",
                Arg.Any<CancellationToken>()
            );
        // A fresh channel gets the FULL wanted set in one create: chat READ, the live tracker, and
        // every community/monetization event the ingest translates.
        await client
            .Received(1)
            .SubscribeAsync(
                "kick-bearer-1",
                Arg.Is<IReadOnlyList<KickEventRequest>>(events =>
                    events.Count == WantedEventNames.Length
                    && WantedEventNames.All(name =>
                        events.Any(e => e.Name == name && e.Version == 1)
                    )
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task An_identity_plane_login_connection_is_never_a_subscription_signal()
    {
        // Logging in WITH Kick is not opting your channel into the bot — only the tenant-scoped
        // integration connect is.
        (
            KickEventSubscriptionWorker worker,
            AuthDbContext db,
            IPlatformChannelProvisioner provisioner,
            IKickApiClient client
        ) = Build();
        SeedConnection(db, broadcasterId: null);

        await worker.TickAsync(CancellationToken.None);

        await provisioner
            .DidNotReceiveWithAnyArgs()
            .GetOrCreateAsync(default, default!, default!, default!, Arg.Any<CancellationToken>());
        await client
            .DidNotReceiveWithAnyArgs()
            .SubscribeAsync(default!, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_fully_subscribed_channel_is_adopted_without_a_duplicate_create()
    {
        (KickEventSubscriptionWorker worker, AuthDbContext db, _, IKickApiClient client) = Build(
            existing:
            [
                .. WantedEventNames.Select(
                    (name, i) => new KickEventSubscription($"s{i}", name, 1, "webhook", 12345)
                ),
            ]
        );
        SeedConnection(db, PrimaryChannel);

        await worker.TickAsync(CancellationToken.None);

        await client
            .DidNotReceiveWithAnyArgs()
            .SubscribeAsync(default!, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_partially_subscribed_channel_gets_only_the_missing_events()
    {
        // A channel subscribed before the newer events shipped self-heals: only the missing events
        // are created — the existing chat leg is never duplicated.
        (KickEventSubscriptionWorker worker, AuthDbContext db, _, IKickApiClient client) = Build(
            existing: [new KickEventSubscription("s1", "chat.message.sent", 1, "webhook", 12345)]
        );
        SeedConnection(db, PrimaryChannel);

        await worker.TickAsync(CancellationToken.None);

        await client
            .Received(1)
            .SubscribeAsync(
                "kick-bearer-1",
                Arg.Is<IReadOnlyList<KickEventRequest>>(events =>
                    events.Count == WantedEventNames.Length - 1
                    && events.All(e => e.Name != "chat.message.sent")
                    && events.Any(e => e.Name == "livestream.status.updated")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_missing_scope_backs_the_tenant_off_instead_of_retrying_every_tick()
    {
        (KickEventSubscriptionWorker worker, AuthDbContext db, _, IKickApiClient client) = Build(
            listOutcome: Result.Failure<IReadOnlyList<KickEventSubscription>>(
                "missing scope",
                "MISSING_SCOPE"
            )
        );
        SeedConnection(db, PrimaryChannel);

        await worker.TickAsync(CancellationToken.None);
        await worker.TickAsync(CancellationToken.None);

        // The first tick discovers the 403; the second must be held by the backoff — one call total.
        await client
            .Received(1)
            .ListEventSubscriptionsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
