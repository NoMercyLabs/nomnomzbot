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
using Microsoft.Extensions.Configuration;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Supporters.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Supporters.Entities;
using NomNomzBot.Domain.Webhooks.Entities;
using NomNomzBot.Domain.Webhooks.Enums;
using NomNomzBot.Infrastructure.Supporters;
using NomNomzBot.Infrastructure.Supporters.Adapters;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Webhooks;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Supporters;

/// <summary>
/// Proves the connection surface (supporter-events.md §5): a connection is the enforced enable-toggle for a
/// provider — upsert validates the source + ingress mode, a webhook secret one-step provisions (then rotates)
/// the inbound endpoint whose ingest URL rides back on the DTO, a socket/ws/poll secret seals onto the
/// connection, reconnect-after-delete leaves a single row (no duplicate), disconnecting retires the
/// provisioned endpoint, and the events list filters + orders. Assertions are on the persisted rows and the
/// returned DTO shape.
/// </summary>
public sealed class SupporterConnectionServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2900-2222-7000-8000-000000000001");
    private static readonly Guid Actor = Guid.Parse("019f2900-2222-7000-8000-0000000000aa");

    private static async Task<(
        SupporterConnectionService Service,
        SupporterTestDbContext Db
    )> BuildAsync()
    {
        SupporterTestDbContext db = SupporterTestDbContext.New();
        db.Channels.Add(
            new Channel
            {
                Id = Tenant,
                TwitchChannelId = "1001",
                OwnerUserId = Guid.NewGuid(),
                Name = "c",
                NameNormalized = "c",
            }
        );
        await db.SaveChangesAsync();

        // The REAL endpoint service (over the same db + a transparent protector), so one-step provisioning is
        // proven by the actual endpoint row + its ingest URL — not a mock's say-so.
        ISubjectKeyService subjectKeys = Substitute.For<ISubjectKeyService>();
        subjectKeys
            .GetOrCreateSubjectKeyAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(Guid.NewGuid()));
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["App:BaseUrl"] = "https://bot.example.test" }
            )
            .Build();
        InboundWebhookEndpointService endpoints = new(
            db,
            new PrefixingFakeProtector(),
            subjectKeys,
            config,
            TimeProvider.System,
            new RecordingEventBus()
        );

        return (
            new SupporterConnectionService(
                db,
                [new KofiSupporterSource(), new DonordriveSupporterSource()],
                new PrefixingFakeProtector(),
                endpoints
            ),
            db
        );
    }

    /// <summary>
    /// A transparent stand-in for the AEAD protector: seals as <c>sealed:&lt;plaintext&gt;</c>. The service's
    /// behavior under test is WHAT it stores/keeps (the protector's output, preserved across re-upserts) —
    /// the envelope crypto itself is proven by the crypto suites.
    /// </summary>
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

    [Fact]
    public async Task UpsertAsync_CreatesEnabledKofiConnection()
    {
        (SupporterConnectionService service, SupporterTestDbContext db) = await BuildAsync();

        Result<SupporterConnectionDto> result = await service.UpsertAsync(
            Tenant,
            Actor,
            new UpsertSupporterConnectionRequest("kofi", "webhook", null, null, IsEnabled: true)
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.SourceKey.Should().Be("kofi");
        result.Value.IsEnabled.Should().BeTrue();
        result.Value.HasSecret.Should().BeFalse();

        SupporterConnection stored = await db.SupporterConnections.SingleAsync();
        stored.SourceKey.Should().Be("kofi");
        stored.ConnectionMode.Should().Be("webhook");
        stored.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task UpsertAsync_WithAWebhookSecret_ProvisionsTheInboundEndpointInOneStep()
    {
        (SupporterConnectionService service, SupporterTestDbContext db) = await BuildAsync();

        Result<SupporterConnectionDto> result = await service.UpsertAsync(
            Tenant,
            Actor,
            new UpsertSupporterConnectionRequest("kofi", "webhook", "kofi-verify-token", null, true)
        );

        result.IsSuccess.Should().BeTrue();
        // The real consequence: a live Ko-fi inbound endpoint exists, sealed with the pass-through secret…
        InboundWebhookEndpoint endpoint = await db.InboundWebhookEndpoints.SingleAsync();
        endpoint.AdapterKind.Should().Be(WebhookAdapterKind.Kofi);
        endpoint.IsEnabled.Should().BeTrue();
        endpoint.VerificationSecretEnvelope.Should().Be("sealed:kofi-verify-token");
        // …linked from the connection (which itself stores NO secret)…
        SupporterConnection stored = await db.SupporterConnections.SingleAsync();
        stored.InboundWebhookEndpointId.Should().Be(endpoint.Id);
        stored
            .AuthSecretCipher.Should()
            .BeNull("the webhook secret lives on the endpoint, never here");
        // …and the ingest URL to paste into Ko-fi rides back on the DTO.
        result
            .Value.EndpointUrl.Should()
            .Be($"https://bot.example.test/api/v1/webhooks/in/{endpoint.Token}");
        result.Value.HasSecret.Should().BeTrue();
    }

    [Fact]
    public async Task UpsertAsync_ASecondSecret_RotatesTheSameEndpoint_NeverDuplicates()
    {
        (SupporterConnectionService service, SupporterTestDbContext db) = await BuildAsync();
        await service.UpsertAsync(
            Tenant,
            Actor,
            new UpsertSupporterConnectionRequest("kofi", "webhook", "first-token", null, true)
        );

        Result<SupporterConnectionDto> rotated = await service.UpsertAsync(
            Tenant,
            Actor,
            new UpsertSupporterConnectionRequest("kofi", "webhook", "second-token", null, true)
        );

        rotated.IsSuccess.Should().BeTrue();
        InboundWebhookEndpoint endpoint = await db.InboundWebhookEndpoints.SingleAsync();
        endpoint.VerificationSecretEnvelope.Should().Be("sealed:second-token");
    }

    [Fact]
    public async Task DeleteAsync_RetiresTheProvisionedEndpointWithTheConnection()
    {
        (SupporterConnectionService service, SupporterTestDbContext db) = await BuildAsync();
        await service.UpsertAsync(
            Tenant,
            Actor,
            new UpsertSupporterConnectionRequest("kofi", "webhook", "kofi-verify-token", null, true)
        );

        Result deleted = await service.DeleteAsync(Tenant, Actor, "kofi");

        deleted.IsSuccess.Should().BeTrue();
        // The dead ingest URL stops resolving: the auto-provisioned endpoint is soft-deleted too.
        (await db.InboundWebhookEndpoints.SingleAsync())
            .DeletedAt.Should()
            .NotBeNull();
    }

    [Fact]
    public async Task UpsertAsync_RejectsUnknownSourceAndWrongMode()
    {
        (SupporterConnectionService service, _) = await BuildAsync();

        Result<SupporterConnectionDto> unknown = await service.UpsertAsync(
            Tenant,
            Actor,
            new UpsertSupporterConnectionRequest("bogus", "webhook", null, null, true)
        );
        unknown.IsFailure.Should().BeTrue();
        unknown.ErrorCode.Should().Be("VALIDATION_FAILED");

        // Ko-fi ingests via webhook, not socket — a mismatched mode is rejected.
        Result<SupporterConnectionDto> wrongMode = await service.UpsertAsync(
            Tenant,
            Actor,
            new UpsertSupporterConnectionRequest("kofi", "socket", null, null, true)
        );
        wrongMode.IsFailure.Should().BeTrue();
        wrongMode.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    [Fact]
    public async Task UpsertAsync_SealsAPollProviderSecret_AndKeepsItWhenOmittedOnReUpsert()
    {
        (SupporterConnectionService service, SupporterTestDbContext db) = await BuildAsync();

        Result<SupporterConnectionDto> created = await service.UpsertAsync(
            Tenant,
            Actor,
            new UpsertSupporterConnectionRequest(
                "donordrive",
                "poll",
                "https://www.extra-life.org/api/participants/12345/donations",
                null,
                IsEnabled: true
            )
        );

        created.IsSuccess.Should().BeTrue();
        created.Value.HasSecret.Should().BeTrue();
        SupporterConnection stored = await db.SupporterConnections.SingleAsync();
        // Sealed via the protector — never the plaintext feed URL in the column.
        stored
            .AuthSecretCipher.Should()
            .Be("sealed:https://www.extra-life.org/api/participants/12345/donations");

        // A later toggle that omits the secret must keep the stored credential, not wipe it.
        Result<SupporterConnectionDto> toggled = await service.UpsertAsync(
            Tenant,
            Actor,
            new UpsertSupporterConnectionRequest("donordrive", "poll", null, null, IsEnabled: false)
        );

        toggled.IsSuccess.Should().BeTrue();
        toggled.Value.HasSecret.Should().BeTrue();
        (await db.SupporterConnections.SingleAsync())
            .AuthSecretCipher.Should()
            .Be("sealed:https://www.extra-life.org/api/participants/12345/donations");
    }

    [Fact]
    public async Task DeleteThenReconnect_LeavesASingleRow()
    {
        (SupporterConnectionService service, SupporterTestDbContext db) = await BuildAsync();
        await service.UpsertAsync(
            Tenant,
            Actor,
            new UpsertSupporterConnectionRequest("kofi", "webhook", null, null, true)
        );

        Result deleted = await service.DeleteAsync(Tenant, Actor, "kofi");
        deleted.IsSuccess.Should().BeTrue();

        Result<SupporterConnectionDto> reconnected = await service.UpsertAsync(
            Tenant,
            Actor,
            new UpsertSupporterConnectionRequest("kofi", "webhook", null, null, true)
        );
        reconnected.IsSuccess.Should().BeTrue();

        // Reconnect must not orphan a second row for the same (broadcaster, source).
        (await db.SupporterConnections.IgnoreQueryFilters().CountAsync())
            .Should()
            .Be(1);
    }

    [Fact]
    public async Task ListEventsAsync_FiltersByKind_NewestFirst()
    {
        (SupporterConnectionService service, SupporterTestDbContext db) = await BuildAsync();
        DateTime baseTime = new(2026, 7, 11, 0, 0, 0, DateTimeKind.Utc);
        db.SupporterEvents.AddRange(
            Event("tip", "t1", baseTime),
            Event("membership", "m1", baseTime.AddMinutes(1)),
            Event("tip", "t2", baseTime.AddMinutes(2))
        );
        await db.SaveChangesAsync();

        Result<PagedList<SupporterEventDto>> tips = await service.ListEventsAsync(
            Tenant,
            new SupporterEventQuery(1, 25, "tip", null)
        );

        tips.IsSuccess.Should().BeTrue();
        tips.Value.TotalCount.Should().Be(2);
        tips.Value.Items.Select(e => e.SupporterDisplayName).Should().Equal("t2", "t1"); // newest first
    }

    private static SupporterEvent Event(string kind, string tx, DateTime receivedAt) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Tenant,
            SourceKey = "kofi",
            Kind = kind,
            SupporterDisplayName = tx,
            ProviderTransactionId = tx,
            ReceivedAt = receivedAt,
        };
}
