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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;
using FakeTimeProvider = Microsoft.Extensions.Time.Testing.FakeTimeProvider;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// Proves the catalog / redemption surface (economy.md §3.4) against the real SQLite ledger harness: create +
/// duplicate rejection; a purchase enforces permission / stock / cooldown / funds, debits the buyer, records a
/// completed purchase, decrements stock, and emits the purchase event; and a refund credits back, restores
/// stock, and writes an append-only refunded row.
/// </summary>
public sealed class CatalogServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000d1");
    private static readonly Guid Buyer = Guid.Parse("0192a000-0000-7000-8000-0000000000d2");
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero)
    );

    private static (CatalogService Sut, EventStoreTestDbContext Db, RecordingEventBus Bus) New(
        SqliteTestDatabase database,
        long startingBalance = 100
    )
    {
        EventStoreTestDbContext db = database.NewContext();
        db.CurrencyConfigs.Add(
            new CurrencyConfig
            {
                BroadcasterId = Channel,
                CurrencyName = "points",
                IsEnabled = true,
                StartingBalance = startingBalance,
            }
        );
        db.SaveChanges();
        EventStoreTestUnitOfWork uow = new(db);
        TenantSequenceAllocator allocator = new(db);
        RecordingEventBus bus = new();
        CurrencyAccountService accounts = new(db, allocator, uow, bus, Clock);
        CatalogService sut = new(db, accounts, bus, Clock);
        return (sut, db, bus);
    }

    private static async Task<Guid> CreateAsync(
        CatalogService sut,
        string name = "Sound Alert",
        long cost = 30,
        string permission = "Everyone",
        int? stock = null,
        int cooldown = 0
    )
    {
        Result<CatalogItemDto> r = await sut.CreateItemAsync(
            Channel,
            new CreateCatalogItemRequest(
                name,
                null,
                "pipeline",
                cost,
                null,
                IsEnabled: true,
                permission,
                PipelineId: null,
                cooldown,
                CooldownPerUser: false,
                stock,
                MaxPerViewerPerStream: null,
                SortOrder: 0
            )
        );
        return r.Value.Id;
    }

    private static PurchaseRequest Buy(Guid itemId, int roleLevel = 0) =>
        new(itemId, Buyer, InputArgs: null, roleLevel, IdempotencyKey: null);

    [Fact]
    public async Task Create_rejects_a_duplicate_name()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CatalogService sut, _, _) = New(database);

        await CreateAsync(sut, name: "Sound Alert");
        Result<CatalogItemDto> dup = await sut.CreateItemAsync(
            Channel,
            new CreateCatalogItemRequest(
                "sound alert",
                null,
                "pipeline",
                10,
                null,
                true,
                "Everyone",
                null,
                0,
                false,
                null,
                null,
                0
            )
        );

        dup.ErrorCode.Should().Be("ALREADY_EXISTS");
    }

    [Fact]
    public async Task Purchase_debits_records_and_decrements_stock()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CatalogService sut, EventStoreTestDbContext db, RecordingEventBus bus) = New(database);
        Guid item = await CreateAsync(sut, cost: 30, stock: 5);

        Result<CatalogPurchaseDto> purchase = await sut.PurchaseAsync(Channel, Buy(item));

        purchase.IsSuccess.Should().BeTrue(purchase.ErrorMessage);
        purchase.Value.Status.Should().Be("Completed");
        purchase.Value.CostPaid.Should().Be(30);
        (
            await new CurrencyAccountService(
                db,
                new TenantSequenceAllocator(db),
                new EventStoreTestUnitOfWork(db),
                bus,
                Clock
            ).GetBalanceAsync(Channel, Buyer)
        )
            .Value.Should()
            .Be(70); // 100 - 30
        (await sut.GetItemAsync(Channel, item)).Value.StockRemaining.Should().Be(4);
        bus.Published.OfType<CatalogItemPurchasedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task Purchase_blocked_below_the_permission_level()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CatalogService sut, _, _) = New(database);
        Guid item = await CreateAsync(sut, cost: 0, permission: "Subscriber"); // level 2

        Result<CatalogPurchaseDto> result = await sut.PurchaseAsync(
            Channel,
            Buy(item, roleLevel: 0)
        );

        result.ErrorCode.Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Purchase_is_out_of_stock_when_depleted()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CatalogService sut, _, _) = New(database);
        Guid item = await CreateAsync(sut, cost: 0, stock: 1);

        (await sut.PurchaseAsync(Channel, Buy(item))).IsSuccess.Should().BeTrue();
        (await sut.PurchaseAsync(Channel, Buy(item))).ErrorCode.Should().Be("OUT_OF_STOCK");
    }

    [Fact]
    public async Task Purchase_respects_the_cooldown()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CatalogService sut, _, _) = New(database);
        Guid item = await CreateAsync(sut, cost: 0, cooldown: 60);

        (await sut.PurchaseAsync(Channel, Buy(item))).IsSuccess.Should().BeTrue();
        (await sut.PurchaseAsync(Channel, Buy(item))).ErrorCode.Should().Be("ON_COOLDOWN");
    }

    [Fact]
    public async Task Purchase_bubbles_insufficient_funds()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CatalogService sut, _, _) = New(database, startingBalance: 10);
        Guid item = await CreateAsync(sut, cost: 50);

        (await sut.PurchaseAsync(Channel, Buy(item))).ErrorCode.Should().Be("INSUFFICIENT_FUNDS");
    }

    [Fact]
    public async Task Refund_credits_back_restores_stock_and_records_a_refunded_row()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CatalogService sut, EventStoreTestDbContext db, RecordingEventBus bus) = New(database);
        Guid item = await CreateAsync(sut, cost: 30, stock: 5);
        Result<CatalogPurchaseDto> purchase = await sut.PurchaseAsync(Channel, Buy(item));

        Result<CatalogPurchaseDto> refund = await sut.RefundPurchaseAsync(
            Channel,
            purchase.Value.Id,
            new RefundRequest("changed mind", Buyer)
        );

        refund.IsSuccess.Should().BeTrue(refund.ErrorMessage);
        refund.Value.Status.Should().Be("Refunded");
        (
            await new CurrencyAccountService(
                db,
                new TenantSequenceAllocator(db),
                new EventStoreTestUnitOfWork(db),
                bus,
                Clock
            ).GetBalanceAsync(Channel, Buyer)
        )
            .Value.Should()
            .Be(100); // refunded back to start
        (await sut.GetItemAsync(Channel, item)).Value.StockRemaining.Should().Be(5);
        bus.Published.OfType<CatalogPurchaseRefundedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task Purchase_enforces_the_per_viewer_per_stream_limit()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        (CatalogService sut, EventStoreTestDbContext db, _) = New(database);
        db.Streams.Add(
            new NomNomzBot.Domain.Stream.Entities.Stream
            {
                Id = "s1",
                ChannelId = Channel,
                StartedAt = new DateTimeOffset(2026, 6, 21, 11, 0, 0, TimeSpan.Zero),
            }
        );
        db.SaveChanges();
        Result<CatalogItemDto> created = await sut.CreateItemAsync(
            Channel,
            new CreateCatalogItemRequest(
                "Hydrate",
                null,
                "pipeline",
                0,
                null,
                true,
                "Everyone",
                null,
                0,
                false,
                null,
                MaxPerViewerPerStream: 1,
                0
            )
        );
        Guid item = created.Value.Id;

        (await sut.PurchaseAsync(Channel, Buy(item))).IsSuccess.Should().BeTrue();
        (await sut.PurchaseAsync(Channel, Buy(item))).ErrorCode.Should().Be("PER_STREAM_LIMIT");
    }
}
