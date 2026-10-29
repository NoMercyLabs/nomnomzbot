// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Economy.Enums;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Seeding;
using NSubstitute;
using FakeTimeProvider = Microsoft.Extensions.Time.Testing.FakeTimeProvider;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// A store item linked to a pipeline runs that pipeline through the real invoker when a purchase is paid: once,
/// for the buyer, with the item data in its variables. A failing pipeline never un-charges or double-charges the
/// buyer, and the failure is logged instead of passing silently.
/// </summary>
public sealed class CatalogPipelinePurchaseTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000e201");
    private static readonly Guid Buyer = Guid.Parse("0192a000-0000-7000-8000-00000000e202");
    private static readonly Guid PipelineId = Guid.Parse("0192a000-0000-7000-8000-00000000e203");
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero));
    private const string Graph = "{\"steps\":[{\"type\":\"send_message\"}]}";

    private sealed record Harness(
        CatalogService Sut,
        EventStoreTestDbContext Db,
        IPipelineEngine Engine,
        ListLogger<CatalogService> Log,
        CurrencyAccountService Accounts
    );

    private static Harness Build(
        SqliteTestDatabase database,
        PipelineOutcome outcome = PipelineOutcome.Completed,
        bool pipelineEnabled = true
    )
    {
        EventStoreTestDbContext db = database.NewContext();
        db.CurrencyConfigs.Add(
            new()
            {
                BroadcasterId = Channel,
                CurrencyName = "points",
                IsEnabled = true,
                StartingBalance = 100,
            }
        );
        db.Users.Add(
            new()
            {
                Id = Buyer,
                TwitchUserId = "twitch-buyer-1",
                Username = "buyer",
                UsernameNormalized = "buyer",
                DisplayName = "Buyer",
            }
        );
        db.Pipelines.Add(
            new PipelineEntity
            {
                Id = PipelineId,
                BroadcasterId = Channel,
                Name = "sound alert",
                TriggerKind = "catalog_item",
                IsEnabled = pipelineEnabled,
                GraphJsonCache = Graph,
            }
        );
        db.SaveChanges();

        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        engine
            .ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new PipelineExecutionResult
                {
                    ExecutionId = "run-1",
                    Outcome = outcome,
                    Duration = TimeSpan.Zero,
                    ErrorMessage = outcome == PipelineOutcome.Failed ? "step broke" : null,
                }
            );

        RecordingEventBus bus = new();
        CurrencyAccountService accounts = new(
            db,
            new TenantSequenceAllocator(db),
            new EventStoreTestUnitOfWork(db),
            bus,
            Clock
        );
        CommandInvoker invoker = new(
            db,
            engine,
            Substitute.For<IOwnerActionService>(),
            Substitute.For<IChannelTwitchBotResolver>(),
            Substitute.For<ITemplateResolver>(),
            Substitute.For<IChatProvider>(),
            Substitute.For<IReplyDispatcher>(),
            Substitute.For<IBuiltinCommandCatalog>(),
            new OutboundSanctionAccessor(),
            Substitute.For<IScriptRunner>()
        );
        ListLogger<CatalogService> log = new();
        CatalogService sut = new(db, accounts, bus, Clock, invoker, log);
        return new(sut, db, engine, log, accounts);
    }

    private static async Task<Guid> CreatePipelineItemAsync(CatalogService sut) =>
        (
            await sut.CreateItemAsync(
                Channel,
                new(
                    "Sound Alert",
                    null,
                    "pipeline",
                    30,
                    null,
                    IsEnabled: true,
                    "Everyone",
                    PipelineId,
                    0,
                    CooldownPerUser: false,
                    null,
                    MaxPerViewerPerStream: null,
                    SortOrder: 0
                )
            )
        )
            .Value
            .Id;

    private static PurchaseRequest Buy(Guid itemId) =>
        new(itemId, Buyer, "airhorn", 0, IdempotencyKey: null);

    [Fact]
    public async Task Purchasing_an_item_linked_to_a_pipeline_runs_it_once_for_the_buyer_with_the_item_data()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Harness h = Build(database);
        Guid itemId = await CreatePipelineItemAsync(h.Sut);

        Result<CatalogPurchaseDto> result = await h.Sut.PurchaseAsync(Channel, Buy(itemId));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        PipelineRequest sent = (PipelineRequest)
            h.Engine.ReceivedCalls().Single().GetArguments()[0]!;
        sent.BroadcasterId.Should().Be(Channel);
        sent.PipelineId.Should().Be(PipelineId);
        sent.PipelineJson.Should().Be(Graph);
        sent.TriggeredByUserId.Should().Be("twitch-buyer-1");
        sent.TriggeredByDisplayName.Should().Be("Buyer");
        sent.RawMessage.Should().Be("airhorn");
        sent.InitialVariables["trigger.source"].Should().Be("catalog");
        sent.InitialVariables["catalog.item.id"].Should().Be(itemId.ToString());
        sent.InitialVariables["catalog.item.name"].Should().Be("Sound Alert");
        sent.InitialVariables["catalog.item.cost"].Should().Be("30");
        sent.InitialVariables["catalog.purchase.id"].Should().Be(result.Value.Id.ToString());
        (await h.Accounts.GetBalanceAsync(Channel, Buyer)).Value.Should().Be(70);
    }

    [Fact]
    public async Task A_pipeline_that_fails_is_logged_and_the_buyer_stays_charged_exactly_once()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Harness h = Build(database, PipelineOutcome.Failed);
        Guid itemId = await CreatePipelineItemAsync(h.Sut);

        Result<CatalogPurchaseDto> result = await h.Sut.PurchaseAsync(Channel, Buy(itemId));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        h.Engine.ReceivedCalls().Should().ContainSingle();
        (await h.Accounts.GetBalanceAsync(Channel, Buyer)).Value.Should().Be(70);
        CatalogPurchase row = await h.Db.CatalogPurchases.SingleAsync();
        row.Status.Should().Be(CatalogPurchaseStatus.Completed);
        h.Log.Entries.Should()
            .Contain(e =>
                e.Level == LogLevel.Warning
                && e.Message.Contains(itemId.ToString())
                && e.Message.Contains("step broke")
            );
    }

    [Fact]
    public async Task A_pipeline_that_throws_is_logged_and_the_paid_purchase_stays_completed()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Harness h = Build(database);
        h.Engine.ExecuteAsync(Arg.Any<PipelineRequest>(), Arg.Any<CancellationToken>())
            .Returns<PipelineExecutionResult>(_ => throw new InvalidOperationException("boom"));
        Guid itemId = await CreatePipelineItemAsync(h.Sut);

        Result<CatalogPurchaseDto> result = await h.Sut.PurchaseAsync(Channel, Buy(itemId));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        (await h.Accounts.GetBalanceAsync(Channel, Buyer)).Value.Should().Be(70);
        (await h.Db.CatalogPurchases.CountAsync()).Should().Be(1);
        h.Log.Entries.Should()
            .Contain(e => e.Level == LogLevel.Error && e.Message.Contains("boom"));
    }

    [Fact]
    public async Task A_disabled_pipeline_is_reported_in_the_log_and_never_reaches_the_engine()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Harness h = Build(database, pipelineEnabled: false);
        Guid itemId = await CreatePipelineItemAsync(h.Sut);

        Result<CatalogPurchaseDto> result = await h.Sut.PurchaseAsync(Channel, Buy(itemId));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        h.Engine.ReceivedCalls().Should().BeEmpty();
        (await h.Accounts.GetBalanceAsync(Channel, Buyer)).Value.Should().Be(70);
        h.Log.Entries.Should().Contain(e => e.Level == LogLevel.Warning);
    }
}
