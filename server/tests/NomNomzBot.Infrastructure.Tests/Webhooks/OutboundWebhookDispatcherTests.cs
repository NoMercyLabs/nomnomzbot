// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Domain.Webhooks.Events;
using NomNomzBot.Infrastructure.Tests.Discord;
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
        IWebhookBodyTemplateRenderer template = Substitute.For<IWebhookBodyTemplateRenderer>();
        template
            .Render(
                Arg.Any<string?>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<bool>()
            )
            .Returns(ci => ci.ArgAt<string?>(0) ?? string.Empty);
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new(new StubHandler(status)));
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

    /// <summary>Captures the body actually put on the wire, so a test can assert what the receiver gets.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    /// <summary>
    /// Builds the dispatcher with the REAL <see cref="WebhookBodyTemplateRenderer"/> and a handler that
    /// captures the outgoing body. The other overload substitutes the renderer, so those tests can never
    /// see whether the real one is wired in or what it emits — the JSON-safety proof has to enter here,
    /// on the path a real delivery takes.
    /// </summary>
    private static (
        OutboundWebhookDispatcher Sut,
        AuthDbContext Db,
        CapturingHandler Handler
    ) BuildWithRealRenderer()
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
        CapturingHandler handler = new();
        IHttpClientFactory factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));
        OutboundWebhookDispatcher sut = new(
            db,
            protector,
            new OutboundWebhookSigner(),
            new WebhookBodyTemplateRenderer(DiscordTemplateTestSupport.CreateResolver()),
            factory,
            new RecordingEventBus(),
            new FakeTimeProvider(Now)
        );
        return (sut, db, handler);
    }

    // The renderer's own tests prove JSON-aware substitution in isolation. This proves it on the path a
    // real webhook takes: a template saved on the endpoint, rendered by the REAL renderer, with the body
    // read back off the outgoing HTTP request. A viewer display name carrying a quote, a backslash, a
    // newline and an emoji must not be able to break the payload the receiver parses.
    [Fact]
    public async Task A_delivered_body_is_valid_JSON_with_a_hostile_viewer_name_escaped()
    {
        (OutboundWebhookDispatcher sut, AuthDbContext db, CapturingHandler handler) =
            BuildWithRealRenderer();
        Guid endpointId = await SeedEndpointAsync(db);
        OutboundWebhookEndpoint endpoint = await db.OutboundWebhookEndpoints.FirstAsync(e =>
            e.Id == endpointId
        );
        endpoint.BodyTemplate = """{"who":"{user.name}","nested":{"kind":"raid"}}""";
        await db.SaveChangesAsync();

        const string hostile = "ev\"il\\ \n \U0001F389";
        Dictionary<string, string> variables = new() { ["user.name"] = hostile };

        OutboundEnqueueResult result = (
            await sut.EnqueueForEndpointAsync(Channel, endpointId, "test.event", variables, null)
        ).Value;

        result.Status.Should().Be(WebhookDeliveryStatus.Delivered);
        handler.Body.Should().NotBeNull();

        // Parsing IS the assertion: a corrupted payload throws here rather than merely looking wrong.
        JObject parsed = JObject.Parse(handler.Body!);
        parsed["who"]!.Value<string>().Should().Be(hostile);
        parsed["nested"]!["kind"]!.Value<string>().Should().Be("raid");
    }

    // A JSON-declared template is validated at save time (OutboundWebhookEndpointServiceTests), so this path
    // should be unreachable in practice — but proves the honest fallback if a bad template somehow reaches
    // storage anyway (S-WEBHOOK-JSON-FALLBACK): the delivery fails with a recorded reason instead of silently
    // rendering through the unescaped plain-text path, and no HTTP request is ever sent.
    [Fact]
    public async Task A_stored_JSON_declared_template_that_fails_to_parse_fails_delivery_honestly_without_sending()
    {
        (OutboundWebhookDispatcher sut, AuthDbContext db, CapturingHandler handler) =
            BuildWithRealRenderer();
        Guid endpointId = await SeedEndpointAsync(db);
        OutboundWebhookEndpoint endpoint = await db.OutboundWebhookEndpoints.FirstAsync(e =>
            e.Id == endpointId
        );
        endpoint.BodyTemplate = /*lang=json,strict*/
            """{"who" "{user.name}"}"""; // missing colon — bypasses save-time validation by direct seeding
        endpoint.BodyIsJson = true;
        await db.SaveChangesAsync();

        OutboundEnqueueResult result = (
            await sut.EnqueueForEndpointAsync(
                Channel,
                endpointId,
                "test.event",
                new Dictionary<string, string> { ["user.name"] = "Stoney_Eagle" },
                null
            )
        ).Value;

        result.Status.Should().Be(WebhookDeliveryStatus.Failed);
        OutboundWebhookDelivery delivery = await db.OutboundWebhookDeliveries.SingleAsync(d =>
            d.Id == result.DeliveryId
        );
        delivery.Error.Should().Contain("line"); // names the parse position
        delivery.RenderedBody.Should().BeEmpty();
        handler.Body.Should().BeNull(); // never sent — no HTTP request left the process
    }

    // A non-JSON endpoint renders through the plain-text path, so labelling its body "application/json"
    // would be a lie the receiver acts on: it would try to parse plain text as JSON and fail.
    [Fact]
    public async Task A_non_JSON_endpoint_is_not_labelled_as_JSON_to_the_receiver()
    {
        (OutboundWebhookDispatcher sut, AuthDbContext db, CapturingHandler handler) =
            BuildWithRealRenderer();
        Guid endpointId = await SeedEndpointAsync(db);
        OutboundWebhookEndpoint endpoint = await db.OutboundWebhookEndpoints.FirstAsync(e =>
            e.Id == endpointId
        );
        endpoint.BodyIsJson = false;
        endpoint.BodyTemplate = "plain body for {user.name}";
        await db.SaveChangesAsync();

        await sut.EnqueueForEndpointAsync(
            Channel,
            endpointId,
            "test.event",
            new Dictionary<string, string> { ["user.name"] = "qtkitte" },
            null
        );

        handler.ContentType.Should().Be("text/plain");
        handler.Body.Should().Be("plain body for qtkitte");
    }
}
