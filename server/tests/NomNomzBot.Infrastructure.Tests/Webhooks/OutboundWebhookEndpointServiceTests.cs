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
using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
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
        RecordingEventBus Bus
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
        return (
            new OutboundWebhookEndpointService(db, protector, keys, new FakeTimeProvider(Now), bus),
            db,
            bus
        );
    }

    private static async Task SeedAllowlistAsync(AuthDbContext db, string fqdn = "api.example.com")
    {
        db.HttpEgressAllowlists.Add(
            new HttpEgressAllowlist
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
        (OutboundWebhookEndpointService sut, _, RecordingEventBus bus) = Build();

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
        (OutboundWebhookEndpointService sut, AuthDbContext db, RecordingEventBus bus) = Build();
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
    public void GetEventCatalogue_returns_curated_subscribable_business_events()
    {
        (OutboundWebhookEndpointService sut, _, _) = Build();

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
        (OutboundWebhookEndpointService sut, AuthDbContext db, _) = Build();
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
        (OutboundWebhookEndpointService sut, AuthDbContext db, _) = Build();
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
        (OutboundWebhookEndpointService sut, AuthDbContext db, RecordingEventBus bus) = Build();
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
        (OutboundWebhookEndpointService sut, AuthDbContext db, _) = Build();
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
        (OutboundWebhookEndpointService sut, AuthDbContext db, _) = Build();
        await SeedAllowlistAsync(db);
        Guid endpointId = (await sut.CreateAsync(Channel, Actor, Req())).Value.Endpoint.Id;

        Result<OutboundWebhookEndpointDto> result = await sut.UpdateAsync(
            Channel,
            endpointId,
            new UpdateOutboundWebhookRequest { SubscribedEventTypes = ["FollowEvent", "bogus"] }
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
        (OutboundWebhookEndpointService sut, AuthDbContext db, _) = Build();
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
        (OutboundWebhookEndpointService sut, AuthDbContext db, _) = Build();
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
        (OutboundWebhookEndpointService sut, AuthDbContext db, _) = Build();
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
        (OutboundWebhookEndpointService sut, AuthDbContext db, _) = Build();
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
            new PaginationParams(1, 10, null, null)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Items[0].EventType.Should().Be("webhook.newer"); // ordered by id descending
        result.Value.Items[0].Status.Should().Be("Delivered");
    }

    [Fact]
    public async Task ListDeliveries_is_NOT_FOUND_for_an_unknown_endpoint()
    {
        (OutboundWebhookEndpointService sut, AuthDbContext db, _) = Build();
        await SeedAllowlistAsync(db);
        await sut.CreateAsync(Channel, Actor, Req());

        Result<PagedList<OutboundWebhookDeliveryDto>> result = await sut.ListDeliveriesAsync(
            Channel,
            Guid.NewGuid(),
            new PaginationParams(1, 10, null, null)
        );

        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
