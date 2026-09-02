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
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Webhooks;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Webhooks;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Webhooks;

/// <summary>
/// Proves outbound webhook endpoint CRUD (webhooks.md §3.5): create fails closed unless the Fqdn matches an enabled
/// H.7 egress-allowlist row, otherwise it seals the minted whsec_ secret (revealing plaintext once) and pins the
/// allowlist row; rotate promotes the primary to secondary and mints a fresh primary; re-enable clears the failure
/// counters; and the synthetic test delivery is unavailable pending the egress client.
/// </summary>
public sealed class OutboundWebhookEndpointServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000c01");
    private static readonly Guid Actor = Guid.Parse("0192a000-0000-7000-8000-000000000c02");
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private static (
        OutboundWebhookEndpointService Sut,
        AuthDbContext Db,
        RecordingEventBus Bus,
        IOutboundWebhookDispatcher Dispatcher
    ) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITokenProtector protector = Substitute.For<ITokenProtector>();
        protector
            .ProtectAsync(
                Arg.Any<string>(),
                Arg.Any<TokenProtectionContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci => Task.FromResult($"sealed:{ci.ArgAt<string>(0)}"));
        ISubjectKeyService keys = Substitute.For<ISubjectKeyService>();
        keys.GetOrCreateSubjectKeyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Guid.Parse("0192a000-0000-7000-8000-0000000000cc")));
        RecordingEventBus bus = new();
        ITemplateHelperValidator templateHelperValidator = new TemplateHelperValidator();
        // The real dispatcher mutates the delivery's Status/NextRetryAt in place AND returns the resulting
        // status (OutboundWebhookDispatcher.AttemptDeliveryAsync) — the mock mirrors both halves of that
        // contract so a caller reading either the returned status or the delivery entity sees the same thing.
        IOutboundWebhookDispatcher dispatcher = Substitute.For<IOutboundWebhookDispatcher>();
        dispatcher
            .AttemptDeliveryAsync(Arg.Any<OutboundWebhookDelivery>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                OutboundWebhookDelivery delivery = ci.ArgAt<OutboundWebhookDelivery>(0);
                delivery.Status = WebhookDeliveryStatus.Delivered;
                delivery.NextRetryAt = null;
                return Task.FromResult(Result.Success(WebhookDeliveryStatus.Delivered));
            });
        return (
            new(
                db,
                protector,
                keys,
                new FakeTimeProvider(Now),
                bus,
                templateHelperValidator,
                dispatcher
            ),
            db,
            bus,
            dispatcher
        );
    }

    private static async Task SeedAllowlistAsync(AuthDbContext db, string fqdn = "api.example.com")
    {
        db.HttpEgressAllowlists.Add(
            new()
            {
                BroadcasterId = Channel,
                Fqdn = fqdn,
                IsEnabled = true,
                MaxResponseBytes = 65536,
                CreatedAt = Now.UtcDateTime,
                UpdatedAt = Now.UtcDateTime,
            }
        );
        await db.SaveChangesAsync();
    }

    private static CreateOutboundWebhookRequest Req(string fqdn = "api.example.com") =>
        new()
        {
            Name = "endpoint",
            Fqdn = fqdn,
            SubscribedEventTypes = ["*"],
        };

    private static CreateOutboundWebhookRequest ReqWith(params string[] eventTypes) =>
        new()
        {
            Name = "endpoint",
            Fqdn = "api.example.com",
            SubscribedEventTypes = [.. eventTypes],
        };

    [Fact]
    public async Task Create_fails_closed_without_an_egress_allowlist_row()
    {
        (OutboundWebhookEndpointService sut, _, RecordingEventBus bus, _) = Build();

        Result<OutboundWebhookEndpointCreatedDto> result = await sut.CreateAsync(
            Channel,
            Actor,
            Req()
        );

        result.ErrorCode.Should().Be("EGRESS_NOT_ALLOWED");
        bus.Published.Should().BeEmpty(); // a failed mutation publishes nothing
    }

    [Fact]
    public async Task Create_seals_the_secret_pins_the_allowlist_and_reveals_plaintext_once()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, RecordingEventBus bus, _) = Build();
        await SeedAllowlistAsync(db);

        OutboundWebhookEndpointCreatedDto created = (
            await sut.CreateAsync(Channel, Actor, Req())
        ).Value;

        created.SigningSecret.Should().StartWith("whsec_");
        created.Endpoint.SubscribedEventTypes.Should().Contain("*");
        OutboundWebhookEndpoint stored = db.OutboundWebhookEndpoints.Single();
        stored.SigningSecretEnvelope.Should().StartWith("sealed:whsec_"); // sealed, not plaintext
        stored.HttpEgressAllowlistId.Should().NotBeNull();
        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e =>
                e.BroadcasterId == Channel
                && e.Domain == "webhooks"
                && e.EntityId == created.Endpoint.Id.ToString()
                && e.Action == "created"
            );
    }

    [Fact]
    public async Task Create_rejects_a_JSON_declared_body_template_that_fails_to_parse()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, RecordingEventBus bus, _) = Build();
        await SeedAllowlistAsync(db);
        CreateOutboundWebhookRequest request = new()
        {
            Name = "endpoint",
            Fqdn = "api.example.com",
            SubscribedEventTypes = ["*"],
            BodyTemplate = /*lang=json,strict*/
                """{"who" "{user.name}"}""", // missing colon — invalid JSON
            BodyIsJson = true,
        };

        Result<OutboundWebhookEndpointCreatedDto> result = await sut.CreateAsync(
            Channel,
            Actor,
            request
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("INVALID_JSON_BODY_TEMPLATE");
        result.ErrorMessage.Should().Contain("line"); // names the parse position
        db.OutboundWebhookEndpoints.Should().BeEmpty(); // rejected, nothing persisted
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_accepts_a_body_template_declared_as_non_JSON_even_if_malformed_as_JSON()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, _, _) = Build();
        await SeedAllowlistAsync(db);
        CreateOutboundWebhookRequest request = new()
        {
            Name = "endpoint",
            Fqdn = "api.example.com",
            SubscribedEventTypes = ["*"],
            BodyTemplate = "user={user}&note={", // not JSON at all, and never intended to be
            BodyIsJson = false,
        };

        Result<OutboundWebhookEndpointCreatedDto> result = await sut.CreateAsync(
            Channel,
            Actor,
            request
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Endpoint.BodyIsJson.Should().BeFalse();
    }

    [Fact]
    public async Task Create_rejects_a_body_template_with_an_unknown_helper_key()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, RecordingEventBus bus, _) = Build();
        await SeedAllowlistAsync(db);
        CreateOutboundWebhookRequest request = new()
        {
            Name = "endpoint",
            Fqdn = "api.example.com",
            SubscribedEventTypes = ["*"],
            BodyTemplate = /*lang=json,strict*/
                """{"who": "{user.nmae}"}""", // misspelled helper key
            BodyIsJson = true,
        };

        Result<OutboundWebhookEndpointCreatedDto> result = await sut.CreateAsync(
            Channel,
            Actor,
            request
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("user.nmae"); // names the bad key
        db.OutboundWebhookEndpoints.Should().BeEmpty(); // rejected, nothing persisted
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_saves_a_body_template_whose_helper_keys_are_all_valid()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, RecordingEventBus bus, _) = Build();
        await SeedAllowlistAsync(db);
        CreateOutboundWebhookRequest request = new()
        {
            Name = "endpoint",
            Fqdn = "api.example.com",
            SubscribedEventTypes = ["*"],
            BodyTemplate = /*lang=json,strict*/
                """{"who": "{user.name}", "channel": "{channel.display}"}""",
            BodyIsJson = true,
        };

        Result<OutboundWebhookEndpointCreatedDto> result = await sut.CreateAsync(
            Channel,
            Actor,
            request
        );

        result.IsSuccess.Should().BeTrue();
        OutboundWebhookEndpoint stored = await db.OutboundWebhookEndpoints.SingleAsync(e =>
            e.Id == result.Value.Endpoint.Id
        );
        stored.BodyTemplate.Should().Be(request.BodyTemplate);
        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e => e.Action == "created");
    }

    [Fact]
    public async Task GetAsync_returns_the_currently_saved_body_template()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, _, _) = Build();
        await SeedAllowlistAsync(db);
        const string bodyTemplate = /*lang=json,strict*/
            """{"who": "{user.name}", "channel": "{channel.display}"}""";
        CreateOutboundWebhookRequest request = new()
        {
            Name = "endpoint",
            Fqdn = "api.example.com",
            SubscribedEventTypes = ["*"],
            BodyTemplate = bodyTemplate,
            BodyIsJson = true,
        };
        Guid endpointId = (await sut.CreateAsync(Channel, Actor, request)).Value.Endpoint.Id;

        Result<OutboundWebhookEndpointDto> got = await sut.GetAsync(Channel, endpointId);

        got.IsSuccess.Should().BeTrue();
        got.Value.BodyTemplate.Should().Be(bodyTemplate);
    }

    [Fact]
    public async Task ListAsync_returns_the_currently_saved_body_template()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, _, _) = Build();
        await SeedAllowlistAsync(db);
        const string bodyTemplate = /*lang=json,strict*/
            """{"who": "{user.name}"}""";
        CreateOutboundWebhookRequest request = new()
        {
            Name = "endpoint",
            Fqdn = "api.example.com",
            SubscribedEventTypes = ["*"],
            BodyTemplate = bodyTemplate,
            BodyIsJson = true,
        };
        await sut.CreateAsync(Channel, Actor, request);

        Result<PagedList<OutboundWebhookEndpointDto>> listed = await sut.ListAsync(
            Channel,
            new PaginationParams(1, 25)
        );

        listed.IsSuccess.Should().BeTrue();
        listed.Value.Items.Should().ContainSingle(e => e.BodyTemplate == bodyTemplate);
    }

    [Fact]
    public async Task Update_rejects_a_body_template_with_an_unknown_helper_key()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, _, _) = Build();
        await SeedAllowlistAsync(db);
        OutboundWebhookEndpointCreatedDto created = (
            await sut.CreateAsync(Channel, Actor, Req())
        ).Value;

        Result<OutboundWebhookEndpointDto> result = await sut.UpdateAsync(
            Channel,
            created.Endpoint.Id,
            new UpdateOutboundWebhookRequest
            {
                BodyTemplate = "hello {user.nmae}",
                BodyIsJson = false,
            }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("user.nmae");
        OutboundWebhookEndpoint stored = await db.OutboundWebhookEndpoints.SingleAsync(e =>
            e.Id == created.Endpoint.Id
        );
        stored.BodyTemplate.Should().BeNull(); // rejected update never touched the stored template
    }

    [Fact]
    public async Task Update_rejects_a_JSON_declared_body_template_that_fails_to_parse()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, _, _) = Build();
        await SeedAllowlistAsync(db);
        OutboundWebhookEndpointCreatedDto created = (
            await sut.CreateAsync(Channel, Actor, Req())
        ).Value;

        Result<OutboundWebhookEndpointDto> result = await sut.UpdateAsync(
            Channel,
            created.Endpoint.Id,
            new UpdateOutboundWebhookRequest
            {
                BodyTemplate = /*lang=json,strict*/
                    """{"broken": [1, 2,}""",
                BodyIsJson = true,
            }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("INVALID_JSON_BODY_TEMPLATE");
        OutboundWebhookEndpoint stored = await db.OutboundWebhookEndpoints.SingleAsync(e =>
            e.Id == created.Endpoint.Id
        );
        stored.BodyTemplate.Should().BeNull(); // rejected update never touched the stored template
    }

    [Fact]
    public void GetEventCatalogue_returns_curated_subscribable_business_events()
    {
        (OutboundWebhookEndpointService sut, _, _, _) = Build();

        Result<IReadOnlyList<OutboundWebhookEventCatalogueEntry>> result = sut.GetEventCatalogue();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeEmpty();
        // Every entry is well-formed (real type + human label + category) — the checklist the dashboard renders.
        result
            .Value.Should()
            .OnlyContain(e =>
                !string.IsNullOrWhiteSpace(e.EventType)
                && !string.IsNullOrWhiteSpace(e.Label)
                && !string.IsNullOrWhiteSpace(e.Category)
            );
        result.Value.Select(e => e.EventType).Should().Contain("FollowEvent");
        // The §9 deny-list is never offered as a subscribable option.
        result.Value.Select(e => e.EventType).Should().NotContain("OutboundWebhookEnqueuedEvent");
    }

    [Fact]
    public async Task Create_accepts_a_valid_catalogue_subset()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, _, _) = Build();
        await SeedAllowlistAsync(db);

        Result<OutboundWebhookEndpointCreatedDto> result = await sut.CreateAsync(
            Channel,
            Actor,
            ReqWith("FollowEvent", "CheerEvent", "RaidEvent")
        );

        result.IsSuccess.Should().BeTrue();
        result
            .Value.Endpoint.SubscribedEventTypes.Should()
            .BeEquivalentTo(["FollowEvent", "CheerEvent", "RaidEvent"]);
    }

    [Fact]
    public async Task Create_accepts_the_wildcard_subscription()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, _, _) = Build();
        await SeedAllowlistAsync(db);

        Result<OutboundWebhookEndpointCreatedDto> result = await sut.CreateAsync(
            Channel,
            Actor,
            ReqWith("*")
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Endpoint.SubscribedEventTypes.Should().ContainSingle().Which.Should().Be("*");
    }

    [Fact]
    public async Task Create_rejects_an_unknown_event_type_naming_the_offender()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, RecordingEventBus bus, _) = Build();
        await SeedAllowlistAsync(db);

        Result<OutboundWebhookEndpointCreatedDto> result = await sut.CreateAsync(
            Channel,
            Actor,
            ReqWith("FollowEvent", "NotARealEvent")
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("NotARealEvent");
        db.OutboundWebhookEndpoints.Should().BeEmpty(); // nothing persisted on a rejected create
        bus.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_rejects_a_webhook_lifecycle_event_type_deny_list()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, _, _) = Build();
        await SeedAllowlistAsync(db);

        Result<OutboundWebhookEndpointCreatedDto> result = await sut.CreateAsync(
            Channel,
            Actor,
            ReqWith("OutboundWebhookEnqueuedEvent")
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Contain("OutboundWebhookEnqueuedEvent");
        result.ErrorMessage.Should().Contain("self-amplification");
        db.OutboundWebhookEndpoints.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_rejects_an_unknown_event_type()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, _, _) = Build();
        await SeedAllowlistAsync(db);
        Guid endpointId = (await sut.CreateAsync(Channel, Actor, Req())).Value.Endpoint.Id;

        Result<OutboundWebhookEndpointDto> result = await sut.UpdateAsync(
            Channel,
            endpointId,
            new() { SubscribedEventTypes = ["FollowEvent", "bogus"] }
        );

        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        // The rejected update did not mutate the stored subscription (still the original '*').
        JsonConvert
            .DeserializeObject<List<string>>(
                db.OutboundWebhookEndpoints.Single().SubscribedEventTypesJson
            )
            .Should()
            .BeEquivalentTo(["*"]);
    }

    [Fact]
    public async Task RotateSecret_promotes_the_primary_to_secondary_and_mints_a_new_primary()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, _, _) = Build();
        await SeedAllowlistAsync(db);
        OutboundWebhookEndpointCreatedDto created = (
            await sut.CreateAsync(Channel, Actor, Req())
        ).Value;
        string originalEnvelope = db.OutboundWebhookEndpoints.Single().SigningSecretEnvelope;

        OutboundWebhookEndpointCreatedDto rotated = (
            await sut.RotateSecretAsync(Channel, created.Endpoint.Id)
        ).Value;

        rotated.SigningSecret.Should().StartWith("whsec_");
        OutboundWebhookEndpoint stored = db.OutboundWebhookEndpoints.Single();
        stored.SecondarySigningSecretEnvelope.Should().Be(originalEnvelope);
        stored.SigningSecretEnvelope.Should().NotBe(originalEnvelope);
    }

    [Fact]
    public async Task Reenable_clears_the_failure_counters()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, _, _) = Build();
        await SeedAllowlistAsync(db);
        OutboundWebhookEndpointCreatedDto created = (
            await sut.CreateAsync(Channel, Actor, Req())
        ).Value;
        OutboundWebhookEndpoint endpoint = db.OutboundWebhookEndpoints.Single();
        endpoint.IsEnabled = false;
        endpoint.ConsecutiveFailureCount = 20;
        endpoint.DisabledAt = Now.UtcDateTime;
        await db.SaveChangesAsync();

        await sut.ReenableAsync(Channel, created.Endpoint.Id);

        OutboundWebhookEndpoint after = db.OutboundWebhookEndpoints.Single();
        after.IsEnabled.Should().BeTrue();
        after.ConsecutiveFailureCount.Should().Be(0);
        after.DisabledAt.Should().BeNull();
    }

    [Fact]
    public async Task SendTest_is_unavailable_pending_the_egress_client()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, _, _) = Build();
        await SeedAllowlistAsync(db);
        OutboundWebhookEndpointCreatedDto created = (
            await sut.CreateAsync(Channel, Actor, Req())
        ).Value;

        (await sut.SendTestAsync(Channel, created.Endpoint.Id))
            .ErrorCode.Should()
            .Be("SERVICE_UNAVAILABLE");
    }

    private static OutboundWebhookDelivery Delivery(
        long id,
        Guid endpointId,
        string eventType,
        WebhookDeliveryStatus status
    ) =>
        new()
        {
            Id = id,
            BroadcasterId = Channel,
            EndpointId = endpointId,
            WebhookMessageId = Guid.Empty,
            EventType = eventType,
            RenderedBody = "{}",
            Attempt = 1,
            Status = status,
            CreatedAt = Now.UtcDateTime,
        };

    [Fact]
    public async Task ListDeliveries_returns_the_endpoints_attempts_newest_first()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, _, _) = Build();
        await SeedAllowlistAsync(db);
        Guid endpointId = (await sut.CreateAsync(Channel, Actor, Req())).Value.Endpoint.Id;
        db.OutboundWebhookDeliveries.AddRange(
            Delivery(1, endpointId, "webhook.older", WebhookDeliveryStatus.Failed),
            Delivery(2, endpointId, "webhook.newer", WebhookDeliveryStatus.Delivered)
        );
        await db.SaveChangesAsync();

        Result<PagedList<OutboundWebhookDeliveryDto>> result = await sut.ListDeliveriesAsync(
            Channel,
            endpointId,
            new(1, 10, null, null)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Items[0].EventType.Should().Be("webhook.newer"); // ordered by id descending
        result.Value.Items[0].Status.Should().Be("Delivered");
    }

    [Fact]
    public async Task ListDeliveries_round_trips_NextRetryAt_status_and_error_text_end_to_end()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, _, _) = Build();
        await SeedAllowlistAsync(db);
        Guid endpointId = (await sut.CreateAsync(Channel, Actor, Req())).Value.Endpoint.Id;
        DateTime nextRetryAt = Now.UtcDateTime.AddSeconds(30);
        OutboundWebhookDelivery pending = Delivery(
            1,
            endpointId,
            "webhook.pending",
            WebhookDeliveryStatus.Pending
        );
        pending.NextRetryAt = nextRetryAt;
        OutboundWebhookDelivery failed = Delivery(
            2,
            endpointId,
            "webhook.failed",
            WebhookDeliveryStatus.Failed
        );
        failed.Error = "Connection timed out after 5000ms";
        failed.NextRetryAt = nextRetryAt;
        OutboundWebhookDelivery deadLettered = Delivery(
            3,
            endpointId,
            "webhook.dead",
            WebhookDeliveryStatus.DeadLetter
        );
        deadLettered.Error = "Endpoint disabled after 20 consecutive failures";
        db.OutboundWebhookDeliveries.AddRange(pending, failed, deadLettered);
        await db.SaveChangesAsync();

        Result<PagedList<OutboundWebhookDeliveryDto>> result = await sut.ListDeliveriesAsync(
            Channel,
            endpointId,
            new(1, 10, null, null)
        );

        result.IsSuccess.Should().BeTrue();
        OutboundWebhookDeliveryDto pendingDto = result.Value.Items.Single(d => d.Id == pending.Id);
        pendingDto.Status.Should().Be("Pending");
        pendingDto.NextRetryAt.Should().Be(nextRetryAt);
        pendingDto.Error.Should().BeNull();

        OutboundWebhookDeliveryDto failedDto = result.Value.Items.Single(d => d.Id == failed.Id);
        failedDto.Status.Should().Be("Failed");
        failedDto.NextRetryAt.Should().Be(nextRetryAt);
        failedDto.Error.Should().Be("Connection timed out after 5000ms");

        OutboundWebhookDeliveryDto deadDto = result.Value.Items.Single(d =>
            d.Id == deadLettered.Id
        );
        deadDto.Status.Should().Be("DeadLetter");
        deadDto.NextRetryAt.Should().BeNull(); // dead-lettered: no further retry is scheduled
        deadDto.Error.Should().Be("Endpoint disabled after 20 consecutive failures");
    }

    [Fact]
    public async Task ListDeliveries_is_NOT_FOUND_for_an_unknown_endpoint()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, _, _) = Build();
        await SeedAllowlistAsync(db);
        await sut.CreateAsync(Channel, Actor, Req());

        Result<PagedList<OutboundWebhookDeliveryDto>> result = await sut.ListDeliveriesAsync(
            Channel,
            Guid.NewGuid(),
            new(1, 10, null, null)
        );

        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task RetryDelivery_re_attempts_using_the_already_stored_RenderedBody_not_a_re_render()
    {
        (
            OutboundWebhookEndpointService sut,
            AuthDbContext db,
            _,
            IOutboundWebhookDispatcher dispatcher
        ) = Build();
        await SeedAllowlistAsync(db);
        OutboundWebhookEndpointCreatedDto created = (
            await sut.CreateAsync(
                Channel,
                Actor,
                new()
                {
                    Name = "endpoint",
                    Fqdn = "api.example.com",
                    SubscribedEventTypes = ["*"],
                    // The template on the endpoint TODAY differs from what was rendered at enqueue time — a
                    // retry must resend the frozen RenderedBody below, never re-render this current template.
                    BodyTemplate = """{"who": "{user.name}"}""",
                    BodyIsJson = true,
                }
            )
        ).Value;
        OutboundWebhookDelivery delivery = Delivery(
            1,
            created.Endpoint.Id,
            "webhook.retry",
            WebhookDeliveryStatus.Failed
        );
        delivery.RenderedBody = """{"who": "frozen-at-enqueue-time"}""";
        db.OutboundWebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        Result<OutboundWebhookDeliveryDto> result = await sut.RetryDeliveryAsync(
            Channel,
            created.Endpoint.Id,
            delivery.Id
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(nameof(WebhookDeliveryStatus.Delivered));
        await dispatcher
            .Received(1)
            .AttemptDeliveryAsync(
                Arg.Is<OutboundWebhookDelivery>(d =>
                    d.Id == delivery.Id && d.RenderedBody == """{"who": "frozen-at-enqueue-time"}"""
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RetryDelivery_is_NOT_FOUND_for_a_delivery_that_does_not_exist_under_this_endpoint()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, _, _) = Build();
        await SeedAllowlistAsync(db);
        OutboundWebhookEndpointCreatedDto created = (
            await sut.CreateAsync(Channel, Actor, Req())
        ).Value;

        Result<OutboundWebhookDeliveryDto> result = await sut.RetryDeliveryAsync(
            Channel,
            created.Endpoint.Id,
            deliveryId: 999
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task RetryDelivery_is_NOT_FOUND_for_a_delivery_belonging_to_a_different_tenant()
    {
        (
            OutboundWebhookEndpointService sut,
            AuthDbContext db,
            _,
            IOutboundWebhookDispatcher dispatcher
        ) = Build();
        await SeedAllowlistAsync(db);
        OutboundWebhookEndpointCreatedDto created = (
            await sut.CreateAsync(Channel, Actor, Req())
        ).Value;
        Guid otherChannel = Guid.Parse("0192a000-0000-7000-8000-0000000000ff");
        OutboundWebhookDelivery foreignDelivery = new()
        {
            Id = 5,
            BroadcasterId = otherChannel, // a different tenant than the one calling RetryDeliveryAsync
            EndpointId = created.Endpoint.Id,
            WebhookMessageId = Guid.Empty,
            EventType = "webhook.foreign",
            RenderedBody = "{}",
            Attempt = 1,
            Status = WebhookDeliveryStatus.Failed,
            CreatedAt = Now.UtcDateTime,
        };
        db.OutboundWebhookDeliveries.Add(foreignDelivery);
        await db.SaveChangesAsync();

        Result<OutboundWebhookDeliveryDto> result = await sut.RetryDeliveryAsync(
            Channel,
            created.Endpoint.Id,
            foreignDelivery.Id
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
        await dispatcher
            .DidNotReceive()
            .AttemptDeliveryAsync(Arg.Any<OutboundWebhookDelivery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryDelivery_is_refused_with_a_clear_error_when_the_endpoint_is_disabled()
    {
        (
            OutboundWebhookEndpointService sut,
            AuthDbContext db,
            _,
            IOutboundWebhookDispatcher dispatcher
        ) = Build();
        await SeedAllowlistAsync(db);
        OutboundWebhookEndpointCreatedDto created = (
            await sut.CreateAsync(Channel, Actor, Req())
        ).Value;
        OutboundWebhookEndpoint endpoint = db.OutboundWebhookEndpoints.Single();
        endpoint.IsEnabled = false;
        endpoint.DisabledAt = Now.UtcDateTime;
        endpoint.DisabledReason = "Too many consecutive delivery failures.";
        OutboundWebhookDelivery delivery = Delivery(
            7,
            created.Endpoint.Id,
            "webhook.deadlettered",
            WebhookDeliveryStatus.DeadLetter
        );
        db.OutboundWebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync();

        Result<OutboundWebhookDeliveryDto> result = await sut.RetryDeliveryAsync(
            Channel,
            created.Endpoint.Id,
            delivery.Id
        );

        // Refused with a clear, distinct reason — never a silent no-op and never bypassing the disabled guard.
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("ENDPOINT_DISABLED");
        await dispatcher
            .DidNotReceive()
            .AttemptDeliveryAsync(Arg.Any<OutboundWebhookDelivery>(), Arg.Any<CancellationToken>());
    }
}
