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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Supporters.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Supporters.Entities;
using NomNomzBot.Domain.Supporters.Events;
using NomNomzBot.Infrastructure.Supporters;
using NomNomzBot.Infrastructure.Supporters.Adapters;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Supporters;

/// <summary>
/// Proves the single ingest path (supporter-events.md §3): a verified Ko-fi payload against an ENABLED
/// connection persists exactly one <see cref="SupporterEvent"/> with the normalized fields and publishes one
/// <see cref="SupporterEventReceived"/>; a disabled/absent connection ingests nothing; a redelivered
/// transaction inserts once; an unknown source fails. Every assertion is on persisted state + the emitted event.
/// </summary>
public sealed class SupporterIngestServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("019f2900-1111-7000-8000-000000000001");

    private const string Donation = """
        { "type": "Donation", "from_name": "Alice", "amount": "5.00", "currency": "USD", "kofi_transaction_id": "tx-1", "message": "gg" }
        """;

    private static async Task<(
        SupporterIngestService Service,
        SupporterTestDbContext Db,
        IEventBus Bus
    )> BuildAsync(bool withEnabledConnection)
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
        if (withEnabledConnection)
            db.SupporterConnections.Add(
                new SupporterConnection
                {
                    Id = Guid.CreateVersion7(),
                    BroadcasterId = Tenant,
                    SourceKey = "kofi",
                    ConnectionMode = "webhook",
                    IsEnabled = true,
                    Status = "idle",
                }
            );
        await db.SaveChangesAsync();

        IEventBus bus = Substitute.For<IEventBus>();
        SupporterIngestService service = new(
            db,
            [new KofiSupporterSource()],
            bus,
            TimeProvider.System,
            NullLogger<SupporterIngestService>.Instance
        );
        return (service, db, bus);
    }

    [Fact]
    public async Task IngestAsync_EnabledConnection_PersistsEventAndPublishesOnce()
    {
        (SupporterIngestService service, SupporterTestDbContext db, IEventBus bus) =
            await BuildAsync(withEnabledConnection: true);

        Result result = await service.IngestAsync(Tenant, "kofi", Donation);

        result.IsSuccess.Should().BeTrue();

        SupporterEvent stored = await db.SupporterEvents.SingleAsync();
        stored.Kind.Should().Be("tip");
        stored.SourceKey.Should().Be("kofi");
        stored.AmountMinor.Should().Be(500);
        stored.Currency.Should().Be("USD");
        stored.SupporterDisplayName.Should().Be("Alice");
        stored.ProviderTransactionId.Should().Be("tx-1");

        // The connection is stamped active on a real event.
        SupporterConnection connection = await db.SupporterConnections.SingleAsync();
        connection.Status.Should().Be("active");
        connection.LastEventAt.Should().NotBeNull();

        await bus.Received(1)
            .PublishAsync(
                Arg.Is<SupporterEventReceived>(e =>
                    e.Kind == "tip" && e.AmountMinor == 500 && e.SupporterEventId == stored.Id
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task IngestAsync_NoConnection_IsANoOp()
    {
        (SupporterIngestService service, SupporterTestDbContext db, IEventBus bus) =
            await BuildAsync(withEnabledConnection: false);

        Result result = await service.IngestAsync(Tenant, "kofi", Donation);

        result.IsSuccess.Should().BeTrue();
        (await db.SupporterEvents.CountAsync()).Should().Be(0);
        await bus.DidNotReceive()
            .PublishAsync(Arg.Any<SupporterEventReceived>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_DisabledConnection_IsANoOp()
    {
        (SupporterIngestService service, SupporterTestDbContext db, IEventBus bus) =
            await BuildAsync(withEnabledConnection: true);
        SupporterConnection connection = await db.SupporterConnections.SingleAsync();
        connection.IsEnabled = false;
        await db.SaveChangesAsync();

        Result result = await service.IngestAsync(Tenant, "kofi", Donation);

        result.IsSuccess.Should().BeTrue();
        (await db.SupporterEvents.CountAsync()).Should().Be(0);
        await bus.DidNotReceive()
            .PublishAsync(Arg.Any<SupporterEventReceived>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_RedeliveredTransaction_InsertsOnce()
    {
        (SupporterIngestService service, SupporterTestDbContext db, IEventBus bus) =
            await BuildAsync(withEnabledConnection: true);

        await service.IngestAsync(Tenant, "kofi", Donation);
        Result second = await service.IngestAsync(Tenant, "kofi", Donation);

        second.IsSuccess.Should().BeTrue();
        (await db.SupporterEvents.CountAsync()).Should().Be(1); // deduped on the transaction id
        await bus.Received(1)
            .PublishAsync(Arg.Any<SupporterEventReceived>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_UnknownSource_Fails()
    {
        (SupporterIngestService service, _, _) = await BuildAsync(withEnabledConnection: true);

        Result result = await service.IngestAsync(Tenant, "bogus", Donation);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
    }
}
