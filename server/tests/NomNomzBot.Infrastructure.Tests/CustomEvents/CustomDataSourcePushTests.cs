// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Application.DTOs.Webhooks;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.CustomEvents.Entities;
using NomNomzBot.Domain.CustomEvents.Events;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Domain.Webhooks.Events;
using NomNomzBot.Infrastructure.CustomEvents;
using NomNomzBot.Infrastructure.CustomEvents.EventHandlers;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Webhooks;
using NomNomzBot.Infrastructure.Webhooks.Adapters;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomEvents;

/// <summary>
/// Proves the push-source adapter: a push source owns an inbound webhook endpoint (created with the source,
/// rotated on update, deleted with the source or when it stops being a push source), the read DTO carries the
/// endpoint's ingest URL, and a signed POST through the real dispatcher ends as a <c>custom.&lt;name&gt;</c>
/// ingest with the posted fields. Assertions are on the persisted rows and the events on the bus.
/// </summary>
public sealed class CustomDataSourcePushTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2a00-3333-7000-8000-000000000001");
    private static readonly Guid Actor = Guid.Parse("019f2a00-3333-7000-8000-0000000000aa");
    private const string Secret = "push-shared-secret";
    private const string BaseUrl = "https://bot.example.test";

    private sealed record Rig(
        CustomDataSourceService Service,
        AuthDbContext Db,
        InboundWebhookEndpointService Endpoints,
        RecordingEventBus Bus,
        InboundWebhookDispatcher Dispatcher,
        CustomDataPushBridge Bridge
    );

    private static async Task<Rig> BuildAsync()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Tenant,
                TwitchChannelId = "1001",
                OwnerUserId = Guid.NewGuid(),
                Name = "c",
                NameNormalized = "c",
            }
        );
        await db.SaveChangesAsync();

        ISubjectKeyService subjectKeys = Substitute.For<ISubjectKeyService>();
        subjectKeys
            .GetOrCreateSubjectKeyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Guid.NewGuid()));
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:BaseUrl"] = BaseUrl })
            .Build();
        RecordingEventBus bus = new();
        PrefixingFakeProtector protector = new();
        InboundWebhookEndpointService endpoints = new(
            db,
            protector,
            subjectKeys,
            config,
            TimeProvider.System,
            bus
        );

        Dictionary<Guid, EventRecord> stored = [];
        IEventJournal journal = Substitute.For<IEventJournal>();
        journal
            .GetByEventIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
                stored.TryGetValue(ci.ArgAt<Guid>(0), out EventRecord? found)
                    ? Result.Success(found)
                    : Result.Failure<EventRecord>("Not found.", "NOT_FOUND")
            );
        journal
            .AppendAsync(Arg.Any<AppendEventRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                AppendEventRequest request = ci.ArgAt<AppendEventRequest>(0);
                EventRecord record = new(
                    stored.Count + 1,
                    request.EventId,
                    request.BroadcasterId,
                    stored.Count + 1,
                    request.EventType,
                    request.EventVersion,
                    request.Source,
                    request.PayloadJson,
                    false,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    request.MetadataJson,
                    request.OccurredAt,
                    request.OccurredAt
                );
                stored[request.EventId] = record;
                return Result.Success(record);
            });

        InboundWebhookDispatcher dispatcher = new(
            db,
            protector,
            [new GenericInboundWebhookAdapter(new InboundSignatureVerifier(TimeProvider.System))],
            journal,
            bus,
            TimeProvider.System
        );
        CustomDataIngestService ingest = new(db, bus, Substitute.For<ICacheService>());
        CustomDataPushBridge bridge = new(
            db,
            journal,
            ingest,
            NullLogger<CustomDataPushBridge>.Instance
        );
        CustomDataSourceService service = new(
            db,
            protector,
            ingest,
            Substitute.For<ICustomDataEgressFetcher>(),
            [],
            endpoints
        );

        return new(service, db, endpoints, bus, dispatcher, bridge);
    }

    private sealed class PrefixingFakeProtector : ITokenProtector
    {
        public Task<string> ProtectAsync(
            string plaintext,
            TokenProtectionContext context,
            CancellationToken cancellationToken = default
        ) => Task.FromResult($"sealed:{plaintext}");

        public Task<string?> TryUnprotectAsync(
            string? sealedEnvelope,
            TokenProtectionContext context,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                sealedEnvelope is not null && sealedEnvelope.StartsWith("sealed:")
                    ? sealedEnvelope["sealed:".Length..]
                    : null
            );
    }

    private static UpsertCustomDataSourceRequest Request(
        string kind,
        string? secret,
        string name = "heart",
        bool enabled = true
    ) =>
        new(
            Name: name,
            DisplayName: "Heart rate",
            SourceKind: kind,
            PresetKey: null,
            EndpointUrl: null,
            AuthSecret: secret,
            FieldMap: new Dictionary<string, string> { ["bpm"] = "$.data.bpm" },
            PollIntervalSeconds: kind == "poll" ? 30 : null,
            IsEnabled: enabled
        );

    private static InboundWebhookRequest SignedPost(string token, string body, string id)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string signature =
            "sha256="
            + Convert.ToHexStringLower(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(Secret),
                    Encoding.UTF8.GetBytes($"{timestamp}.{body}")
                )
            );
        return new()
        {
            Token = token,
            Method = "POST",
            ContentType = "application/json",
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["x-signature"] = signature,
                ["x-timestamp"] = timestamp.ToString(),
            },
            RawBody = Encoding.UTF8.GetBytes(body),
            ReceivedAtUtc = DateTime.UtcNow,
            RemoteIpHash = "h",
        };
    }

    [Fact]
    public async Task CreateAsync_push_source_provisions_a_generic_endpoint_and_exposes_its_ingest_url()
    {
        Rig rig = await BuildAsync();

        Result<CustomDataSourceDto> created = await rig.Service.CreateAsync(
            Tenant,
            Actor,
            Request("push", Secret)
        );

        created.IsSuccess.Should().BeTrue(created.ErrorMessage);
        CustomDataSource source = await rig.Db.CustomDataSources.SingleAsync();
        source.InboundWebhookEndpointId.Should().NotBeNull();
        InboundWebhookEndpoint endpoint = await rig.Db.InboundWebhookEndpoints.SingleAsync(e =>
            e.Id == source.InboundWebhookEndpointId
        );
        endpoint.BroadcasterId.Should().Be(Tenant);
        endpoint.AdapterKind.Should().Be(WebhookAdapterKind.Generic);
        endpoint.IsEnabled.Should().BeTrue();
        endpoint.DeletedAt.Should().BeNull();
        Result<InboundWebhookEndpointDto> read = await rig.Endpoints.GetAsync(Tenant, endpoint.Id);
        created.Value.InboundUrl.Should().Be(read.Value.IngestUrl);
        created.Value.InboundUrl.Should().StartWith(BaseUrl);
    }

    [Fact]
    public async Task CreateAsync_push_source_without_a_secret_is_refused_and_persists_nothing()
    {
        Rig rig = await BuildAsync();

        Result<CustomDataSourceDto> created = await rig.Service.CreateAsync(
            Tenant,
            Actor,
            Request("push", null)
        );

        created.IsFailure.Should().BeTrue();
        created.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await rig.Db.CustomDataSources.CountAsync()).Should().Be(0);
        (await rig.Db.InboundWebhookEndpoints.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_poll_source_gets_no_endpoint()
    {
        Rig rig = await BuildAsync();

        Result<CustomDataSourceDto> created = await rig.Service.CreateAsync(
            Tenant,
            Actor,
            Request("poll", null)
        );

        created.IsSuccess.Should().BeTrue(created.ErrorMessage);
        created.Value.InboundUrl.Should().BeNull();
        (await rig.Db.CustomDataSources.SingleAsync()).InboundWebhookEndpointId.Should().BeNull();
        (await rig.Db.InboundWebhookEndpoints.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateAsync_push_source_whose_final_save_fails_leaves_no_live_inbound_endpoint()
    {
        Rig rig = await BuildAsync();
        // A real database refusal at the source insert, after the endpoint is already committed.
        await rig.Db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER refuse_source BEFORE INSERT ON CustomDataSources "
                + "BEGIN SELECT RAISE(ABORT, 'source insert refused'); END;"
        );

        Func<Task> create = () => rig.Service.CreateAsync(Tenant, Actor, Request("push", Secret));

        await create.Should().ThrowAsync<DbUpdateException>();
        (await rig.Db.CustomDataSources.CountAsync()).Should().Be(0);
        List<InboundWebhookEndpoint> liveEndpoints = await rig
            .Db.InboundWebhookEndpoints.Where(e => e.DeletedAt == null)
            .ToListAsync();
        liveEndpoints.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_removes_the_push_sources_endpoint()
    {
        Rig rig = await BuildAsync();
        Result<CustomDataSourceDto> created = await rig.Service.CreateAsync(
            Tenant,
            Actor,
            Request("push", Secret)
        );

        Result deleted = await rig.Service.DeleteAsync(Tenant, created.Value.Id, Actor);

        deleted.IsSuccess.Should().BeTrue();
        InboundWebhookEndpoint endpoint = await rig
            .Db.InboundWebhookEndpoints.IgnoreQueryFilters()
            .SingleAsync();
        endpoint.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_switching_push_to_poll_removes_the_endpoint_and_clears_the_id()
    {
        Rig rig = await BuildAsync();
        Result<CustomDataSourceDto> created = await rig.Service.CreateAsync(
            Tenant,
            Actor,
            Request("push", Secret)
        );

        Result<CustomDataSourceDto> updated = await rig.Service.UpdateAsync(
            Tenant,
            created.Value.Id,
            Actor,
            Request("poll", null)
        );

        updated.IsSuccess.Should().BeTrue(updated.ErrorMessage);
        updated.Value.InboundUrl.Should().BeNull();
        (await rig.Db.CustomDataSources.SingleAsync()).InboundWebhookEndpointId.Should().BeNull();
        (await rig.Db.InboundWebhookEndpoints.IgnoreQueryFilters().SingleAsync())
            .DeletedAt.Should()
            .NotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_switching_poll_to_push_creates_the_endpoint()
    {
        Rig rig = await BuildAsync();
        Result<CustomDataSourceDto> created = await rig.Service.CreateAsync(
            Tenant,
            Actor,
            Request("poll", null)
        );

        Result<CustomDataSourceDto> updated = await rig.Service.UpdateAsync(
            Tenant,
            created.Value.Id,
            Actor,
            Request("push", Secret)
        );

        updated.IsSuccess.Should().BeTrue(updated.ErrorMessage);
        updated.Value.InboundUrl.Should().NotBeNullOrEmpty();
        (await rig.Db.CustomDataSources.SingleAsync())
            .InboundWebhookEndpointId.Should()
            .NotBeNull();
    }

    [Fact]
    public async Task A_signed_post_to_the_endpoint_ingests_a_custom_event_with_the_posted_fields()
    {
        Rig rig = await BuildAsync();
        await rig.Service.CreateAsync(Tenant, Actor, Request("push", Secret));
        InboundWebhookEndpoint endpoint = await rig.Db.InboundWebhookEndpoints.SingleAsync();
        string body = "{\"id\":\"evt-1\",\"event\":\"reading\",\"data\":{\"bpm\":72}}";

        Result<InboundDispatchResult> dispatched = await rig.Dispatcher.DispatchAsync(
            SignedPost(endpoint.Token, body, "evt-1")
        );

        dispatched.Value.Verified.Should().BeTrue();
        InboundWebhookReceivedEvent received = rig
            .Bus.Published.OfType<InboundWebhookReceivedEvent>()
            .Single();
        await rig.Bridge.HandleAsync(received);

        CustomDataReceivedEvent custom = rig
            .Bus.Published.OfType<CustomDataReceivedEvent>()
            .Single();
        custom.SourceName.Should().Be("heart");
        custom.BroadcasterId.Should().Be(Tenant);
        custom.Fields.Should().ContainKey("bpm").WhoseValue.Should().Be("72");
        (await rig.Db.CustomDataSources.SingleAsync()).LastReceivedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_webhook_for_an_endpoint_no_source_owns_ingests_nothing()
    {
        Rig rig = await BuildAsync();
        Result<InboundWebhookEndpointDto> other = await rig.Endpoints.CreateAsync(
            Tenant,
            Actor,
            new()
            {
                Name = "other",
                Adapter = WebhookAdapterKind.Generic,
                VerificationSecret = Secret,
                GenericConfig = new(
                    "x-signature",
                    "sha256=",
                    "{timestamp}.{body}",
                    "x-timestamp",
                    null,
                    "$.event",
                    "$.id"
                ),
            }
        );
        InboundWebhookEndpoint endpoint = await rig.Db.InboundWebhookEndpoints.SingleAsync(e =>
            e.Id == other.Value.Id
        );
        string body = "{\"id\":\"evt-2\",\"event\":\"reading\",\"data\":{\"bpm\":80}}";

        await rig.Dispatcher.DispatchAsync(SignedPost(endpoint.Token, body, "evt-2"));
        await rig.Bridge.HandleAsync(
            rig.Bus.Published.OfType<InboundWebhookReceivedEvent>().Single()
        );

        rig.Bus.Published.OfType<CustomDataReceivedEvent>().Should().BeEmpty();
    }
}
