// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Linq;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Domain.Webhooks.Events;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Webhooks;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Webhooks;

/// <summary>
/// Proves outbound webhook delivery (webhooks.md §3.6): a subscribed endpoint receives a signed POST and a 2xx
/// marks it delivered (resetting the failure counter); a non-2xx fails and schedules a retry; the endpoint
/// auto-disables (dead-letter) at the failure threshold; an unsubscribed endpoint is skipped; and the single-
/// endpoint path delivers directly.
/// </summary>
public sealed class OutboundWebhookDispatcherTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000d01");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(status));
    }

    private static (OutboundWebhookDispatcher Sut, AuthDbContext Db, RecordingEventBus Bus) Build(
        HttpStatusCode status = HttpStatusCode.OK
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
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
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory
            .CreateClient(Arg.Any<string>())
            .Returns(_ => new HttpClient(new StubHandler(status)));
        RecordingEventBus bus = new();
        OutboundWebhookDispatcher sut = new(
            db,
            protector,
            new OutboundWebhookSigner(),
            template,
            factory,
            bus,
            new FakeTimeProvider(Now)
        );
        return (sut, db, bus);
    }

    private static async Task<Guid> SeedEndpointAsync(
        AuthDbContext db,
        int failureCount = 0,
        string subscribed = "*"
    )
    {
        OutboundWebhookEndpoint endpoint = new()
        {
            BroadcasterId = Channel,
            Name = "ep",
            Fqdn = "api.example.com",
            SubscribedEventTypesJson = JsonConvert.SerializeObject(new[] { subscribed }),
            SigningSecretEnvelope = "sealed",
            EncryptionKeyId = Guid.Parse("0192a000-0000-7000-8000-0000000000dd"),
            IsEnabled = true,
            ConsecutiveFailureCount = failureCount,
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
        };
        db.OutboundWebhookEndpoints.Add(endpoint);
        await db.SaveChangesAsync();
        return endpoint.Id;
    }

    [Fact]
    public async Task EnqueueForEvent_delivers_to_a_subscribed_endpoint_on_2xx()
    {
        (OutboundWebhookDispatcher sut, AuthDbContext db, RecordingEventBus bus) = Build();
        await SeedEndpointAsync(db);

        IReadOnlyList<OutboundEnqueueResult> results = (
            await sut.EnqueueForEventAsync(
                Channel,
                "test.event",
                new Dictionary<string, string> { ["x"] = "1" },
                null
            )
        ).Value;

        results.Should().ContainSingle();
        results[0].Status.Should().Be(WebhookDeliveryStatus.Delivered);
        db.OutboundWebhookDeliveries.Single().Status.Should().Be(WebhookDeliveryStatus.Delivered);
        db.OutboundWebhookEndpoints.Single().ConsecutiveFailureCount.Should().Be(0);
        bus.Published.OfType<OutboundWebhookEnqueuedEvent>().Should().ContainSingle();
        bus.Published.OfType<OutboundWebhookAttemptedEvent>()
            .Should()
            .ContainSingle(e => e.Status == WebhookDeliveryStatus.Delivered);
    }

    [Fact]
    public async Task A_failed_delivery_schedules_a_retry()
    {
        (OutboundWebhookDispatcher sut, AuthDbContext db, _) = Build(
            HttpStatusCode.InternalServerError
        );
        await SeedEndpointAsync(db);

        IReadOnlyList<OutboundEnqueueResult> results = (
            await sut.EnqueueForEventAsync(
                Channel,
                "test.event",
                new Dictionary<string, string>(),
                null
            )
        ).Value;

        results[0].Status.Should().Be(WebhookDeliveryStatus.Failed);
        OutboundWebhookDelivery delivery = db.OutboundWebhookDeliveries.Single();
        delivery.NextRetryAt.Should().NotBeNull();
        db.OutboundWebhookEndpoints.Single().ConsecutiveFailureCount.Should().Be(1);
    }

    [Fact]
    public async Task The_endpoint_auto_disables_at_the_failure_threshold()
    {
        (OutboundWebhookDispatcher sut, AuthDbContext db, RecordingEventBus bus) = Build(
            HttpStatusCode.InternalServerError
        );
        await SeedEndpointAsync(db, failureCount: 19);

        await sut.EnqueueForEventAsync(
            Channel,
            "test.event",
            new Dictionary<string, string>(),
            null
        );

        OutboundWebhookEndpoint endpoint = db.OutboundWebhookEndpoints.Single();
        endpoint.ConsecutiveFailureCount.Should().Be(20);
        endpoint.IsEnabled.Should().BeFalse();
        endpoint.DisabledAt.Should().NotBeNull();
        db.OutboundWebhookDeliveries.Single().Status.Should().Be(WebhookDeliveryStatus.DeadLetter);
        bus.Published.OfType<OutboundWebhookAutoDisabledEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task EnqueueForEvent_skips_an_unsubscribed_endpoint()
    {
        (OutboundWebhookDispatcher sut, AuthDbContext db, _) = Build();
        await SeedEndpointAsync(db, subscribed: "other.event");

        IReadOnlyList<OutboundEnqueueResult> results = (
            await sut.EnqueueForEventAsync(
                Channel,
                "test.event",
                new Dictionary<string, string>(),
                null
            )
        ).Value;

        results.Should().BeEmpty();
        db.OutboundWebhookDeliveries.Should().BeEmpty();
    }

    [Fact]
    public async Task EnqueueForEndpoint_delivers_to_the_single_endpoint()
    {
        (OutboundWebhookDispatcher sut, AuthDbContext db, _) = Build();
        Guid endpointId = await SeedEndpointAsync(db);

        OutboundEnqueueResult result = (
            await sut.EnqueueForEndpointAsync(
                Channel,
                endpointId,
                "test.event",
                new Dictionary<string, string>(),
                null
            )
        ).Value;

        result.Status.Should().Be(WebhookDeliveryStatus.Delivered);
    }
}
