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
using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Infrastructure.BackgroundServices;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Webhooks;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.BackgroundServices;

/// <summary>
/// Proves the multi-instance fix for the outbound webhook delivery drain: two API instances running
/// against one database (a zero-downtime deploy overlap) must never POST the same webhook twice to the
/// customer's endpoint. The drain is gated by <see cref="IRunOnceGuard"/> exactly like
/// <c>GiveawayClaimSweepWorker</c> — a per-row atomic claim would be finer-grained, but the retry query
/// lives in <c>WebhookRetryProcessor</c>, out of scope for this fix. A non-holder must be a clean no-op:
/// no throw, no error-level log — deploy overlap is normal, not a fault.
/// </summary>
public sealed class WebhookDeliveryWorkerTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000f01");
    private static readonly Guid EndpointId = Guid.Parse("0192a000-0000-7000-8000-000000000f02");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Records every outbound HTTP request across however many dispatchers share it — the stand-in
    /// for "the customer's endpoint" that both API instances would otherwise hit independently.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static (
        WebhookDeliveryWorker Worker,
        ILogger<WebhookDeliveryWorker> Logger
    ) BuildWorker(
        string databaseName,
        ConcurrentDictionary<string, byte> sharedLeaseStore,
        RecordingHandler handler
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext(databaseName);

        ITokenProtector protector = Substitute.For<ITokenProtector>();
        protector
            .TryUnprotectAsync(
                Arg.Any<string>(),
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns("whsec_secret");
        ITemplateEngine template = Substitute.For<ITemplateEngine>();
        template
            .Render(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(ci => ci.ArgAt<string>(0));
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(_ => new(handler));

        RecordingEventBus bus = new();
        OutboundWebhookDispatcher dispatcher = new(
            db,
            protector,
            new OutboundWebhookSigner(),
            template,
            httpClientFactory,
            bus,
            new FakeTimeProvider(Now)
        );
        WebhookRetryProcessor processor = new(db, dispatcher, new FakeTimeProvider(Now));

        ServiceCollection services = new();
        services.AddSingleton(processor);
        services.AddSingleton<IRunOnceGuard>(new SharedFakeRunOnceGuard(sharedLeaseStore));
        ServiceProvider provider = services.BuildServiceProvider();

        ILogger<WebhookDeliveryWorker> logger = Substitute.For<ILogger<WebhookDeliveryWorker>>();
        WebhookDeliveryWorker worker = new(provider, logger);
        return (worker, logger);
    }

    private static async Task SeedDueDeliveryAsync(string databaseName)
    {
        AuthDbContext db = AuthTestBuilder.NewContext(databaseName);
        db.OutboundWebhookEndpoints.Add(
            new()
            {
                Id = EndpointId,
                BroadcasterId = Channel,
                Name = "ep",
                Fqdn = "api.example.com",
                SubscribedEventTypesJson = JsonConvert.SerializeObject(new[] { "*" }),
                SigningSecretEnvelope = "sealed",
                EncryptionKeyId = Guid.Parse("0192a000-0000-7000-8000-000000000f03"),
                IsEnabled = true,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            }
        );
        db.OutboundWebhookDeliveries.Add(
            new()
            {
                BroadcasterId = Channel,
                EndpointId = EndpointId,
                WebhookMessageId = Guid.Parse("0192a000-0000-7000-8000-000000000f04"),
                EventType = "test.event",
                RenderedBody = "{}",
                Attempt = 1,
                Status = WebhookDeliveryStatus.Failed,
                NextRetryAt = Now.UtcDateTime.AddMinutes(-1),
                CreatedAt = Now.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Two_instances_sharing_one_lease_and_one_pending_delivery_produce_exactly_one_post()
    {
        string databaseName = Guid.NewGuid().ToString();
        await SeedDueDeliveryAsync(databaseName);

        ConcurrentDictionary<string, byte> sharedLeaseStore = new();
        RecordingHandler handler = new();

        // Simulate instance A already draining this tick — its lease is on the shared store before
        // instance B's worker ever runs, exactly like two API instances racing the same 30s tick.
        IAsyncDisposable? preHeldLease = await new SharedFakeRunOnceGuard(
            sharedLeaseStore
        ).TryAcquireAsync(
            WebhookDeliveryWorker.LeaseResourceName,
            TimeSpan.FromSeconds(30),
            CancellationToken.None
        );
        preHeldLease.Should().NotBeNull();

        (WebhookDeliveryWorker workerB, ILogger<WebhookDeliveryWorker> loggerB) = BuildWorker(
            databaseName,
            sharedLeaseStore,
            handler
        );

        // Instance B loses the race for this tick: a clean no-op, not a fault.
        await workerB.RunIterationAsync(CancellationToken.None);

        handler.Requests.Should().BeEmpty();
        loggerB
            .DidNotReceive()
            .Log(
                LogLevel.Error,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Any<Exception?>(),
                Arg.Any<Func<object, Exception?, string>>()
            );

        // Instance A's tick finishes and releases the lease — the next tick is free again.
        await preHeldLease!.DisposeAsync();

        await workerB.RunIterationAsync(CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.Host.Should().Be("api.example.com");
    }
}
