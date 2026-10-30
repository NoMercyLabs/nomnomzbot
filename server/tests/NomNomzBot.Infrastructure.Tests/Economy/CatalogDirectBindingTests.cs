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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Economy.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Economy;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Economy;

/// <summary>
/// A catalog item binds a command (or action) directly: a purchase runs it through <see cref="ICommandInvoker"/>
/// with the buyer as the invoker and no wrapper pipeline, an item with no target runs nothing (a pipeline-linked
/// item is covered by <c>CatalogPipelinePurchaseTests</c>), a failing run never unwinds the paid purchase, the command guard lists the item, and the create/update request refuses a
/// command that is not in the channel.
/// </summary>
public sealed class CatalogDirectBindingTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000e101");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000e102");
    private static readonly Guid CommandId = Guid.Parse("0192a000-0000-7000-8000-00000000e103");
    private static readonly Guid Buyer = Guid.Parse("0192a000-0000-7000-8000-00000000e104");

    private sealed record Harness(
        CatalogService Sut,
        AuthDbContext Db,
        ICommandInvoker Invoker,
        RecordingEventBus Bus
    );

    private static Harness Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
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
        db.Commands.Add(
            new()
            {
                Id = CommandId,
                BroadcasterId = Channel,
                Name = "hydrate",
                NameNormalized = "hydrate",
                Tier = "template",
                TemplateResponse = "drink water",
            }
        );
        db.SaveChanges();

        ICurrencyAccountService accounts = Substitute.For<ICurrencyAccountService>();
        accounts
            .PostLedgerEntryAsync(
                Arg.Any<Guid>(),
                Arg.Any<PostLedgerEntryCommand>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Result.Success(
                    new CurrencyLedgerEntryDto(
                        1,
                        1,
                        Guid.CreateVersion7(),
                        Buyer,
                        -30,
                        70,
                        "SpendCatalog",
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        DateTime.UtcNow
                    )
                )
            );
        ICommandInvoker invoker = Substitute.For<ICommandInvoker>();
        invoker
            .InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new InvocationOutcome(true, "drink water", null)));
        RecordingEventBus bus = new();
        CatalogService sut = new(
            db,
            accounts,
            bus,
            TimeProvider.System,
            invoker,
            NullLogger<CatalogService>.Instance
        );
        return new(sut, db, invoker, bus);
    }

    private static CreateCatalogItemRequest Create(
        BindingTarget? target,
        Guid? pipelineId = null
    ) =>
        new(
            "Hydrate",
            null,
            "pipeline",
            30,
            null,
            true,
            "Everyone",
            pipelineId,
            0,
            false,
            null,
            null,
            0,
            target
        );

    private static PurchaseRequest Buy(Guid itemId) => new(itemId, Buyer, "two glasses", 0, null);

    [Fact]
    public async Task Purchasing_an_item_bound_to_a_command_runs_it_with_the_buyer_and_no_pipeline()
    {
        Harness h = Build();
        Guid itemId = (
            await h.Sut.CreateItemAsync(
                Channel,
                Create(new(BindingTargetKind.Command, CommandId.ToString()))
            )
        )
            .Value
            .Id;

        Result<CatalogPurchaseDto> result = await h.Sut.PurchaseAsync(Channel, Buy(itemId));

        result.IsSuccess.Should().BeTrue();
        await h
            .Invoker.Received(1)
            .InvokeAsync(
                Channel,
                Arg.Is<BindingTarget>(t =>
                    t.Kind == BindingTargetKind.Command && t.Id == CommandId.ToString()
                ),
                Arg.Is<InvocationContext>(c =>
                    c.UserId == "twitch-buyer-1"
                    && c.DisplayName == "Buyer"
                    && c.Args == "two glasses"
                    && c.TriggerSource == "catalog"
                ),
                Arg.Any<CancellationToken>()
            );
        (await h.Db.CatalogPurchases.CountAsync(p => p.CatalogItemId == itemId)).Should().Be(1);
    }

    [Fact]
    public async Task Purchasing_an_item_bound_to_an_action_runs_the_action_target()
    {
        Harness h = Build();
        Guid itemId = (
            await h.Sut.CreateItemAsync(
                Channel,
                Create(new(BindingTargetKind.Action, null, "send_chat_message"))
            )
        )
            .Value
            .Id;

        await h.Sut.PurchaseAsync(Channel, Buy(itemId));

        await h
            .Invoker.Received(1)
            .InvokeAsync(
                Channel,
                Arg.Is<BindingTarget>(t =>
                    t.Kind == BindingTargetKind.Action && t.ActionType == "send_chat_message"
                ),
                Arg.Is<InvocationContext>(c => c.TriggerSource == "catalog"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_failing_run_is_logged_and_the_paid_purchase_stays_completed()
    {
        Harness h = Build();
        h.Invoker.InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<Result<InvocationOutcome>>(_ => throw new InvalidOperationException("boom"));
        Guid itemId = (
            await h.Sut.CreateItemAsync(
                Channel,
                Create(new(BindingTargetKind.Command, CommandId.ToString()))
            )
        )
            .Value
            .Id;

        Result<CatalogPurchaseDto> result = await h.Sut.PurchaseAsync(Channel, Buy(itemId));

        result.IsSuccess.Should().BeTrue();
        CatalogPurchase row = await h.Db.CatalogPurchases.SingleAsync(p =>
            p.CatalogItemId == itemId
        );
        row.Status.Should().Be(Domain.Economy.Enums.CatalogPurchaseStatus.Completed);
    }

    [Fact]
    public async Task An_item_with_no_target_never_calls_the_invoker()
    {
        Harness h = Build();
        Guid itemId = (await h.Sut.CreateItemAsync(Channel, Create(null))).Value.Id;

        Result<CatalogPurchaseDto> result = await h.Sut.PurchaseAsync(Channel, Buy(itemId));

        result.IsSuccess.Should().BeTrue();
        await h.Invoker.DidNotReceiveWithAnyArgs().InvokeAsync(default, default!, default!);
    }

    [Fact]
    public async Task Create_with_a_command_of_this_channel_persists_it_and_the_detail_shows_the_target()
    {
        Harness h = Build();

        Result<CatalogItemDto> result = await h.Sut.CreateItemAsync(
            Channel,
            Create(new(BindingTargetKind.Command, CommandId.ToString()))
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.Target.Should().NotBeNull();
        result.Value.Target!.Kind.Should().Be(BindingTargetKind.Command);
        result.Value.Target.Id.Should().Be(CommandId.ToString());
        CatalogItem row = await h.Db.CatalogItems.SingleAsync(i => i.Id == result.Value.Id);
        row.TargetKind.Should().Be(BindingTargetKind.Command);
        row.TargetCommandId.Should().Be(CommandId);
    }

    [Fact]
    public async Task Create_with_a_command_of_another_channel_is_refused_by_the_binder_and_stores_nothing()
    {
        Harness h = Build();
        Guid foreignCommand = Guid.CreateVersion7();
        h.Db.Commands.Add(
            new()
            {
                Id = foreignCommand,
                BroadcasterId = OtherChannel,
                Name = "elsewhere",
                NameNormalized = "elsewhere",
                Tier = "template",
                TemplateResponse = "hi",
            }
        );
        await h.Db.SaveChangesAsync();

        Result<CatalogItemDto> result = await h.Sut.CreateItemAsync(
            Channel,
            Create(new(BindingTargetKind.Command, foreignCommand.ToString()))
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Be("That command does not exist in this channel.");
        (await h.Db.CatalogItems.CountAsync(i => i.BroadcasterId == Channel)).Should().Be(0);
    }

    [Fact]
    public async Task Update_with_a_command_of_another_channel_is_refused_and_the_binding_stays()
    {
        Harness h = Build();
        Guid itemId = (
            await h.Sut.CreateItemAsync(
                Channel,
                Create(new(BindingTargetKind.Command, CommandId.ToString()))
            )
        )
            .Value
            .Id;
        Guid foreignCommand = Guid.CreateVersion7();
        h.Db.Commands.Add(
            new()
            {
                Id = foreignCommand,
                BroadcasterId = OtherChannel,
                Name = "elsewhere",
                NameNormalized = "elsewhere",
                Tier = "template",
                TemplateResponse = "hi",
            }
        );
        await h.Db.SaveChangesAsync();

        Result<CatalogItemDto> result = await h.Sut.UpdateItemAsync(
            Channel,
            itemId,
            new(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                new BindingTarget(BindingTargetKind.Command, foreignCommand.ToString())
            )
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        result.ErrorMessage.Should().Be("That command does not exist in this channel.");
        (await h.Db.CatalogItems.SingleAsync(i => i.Id == itemId))
            .TargetCommandId.Should()
            .Be(CommandId);
    }

    [Fact]
    public async Task Deleting_a_command_a_catalog_item_runs_is_refused_and_lists_the_item()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Channel,
                Name = "catalog-binding",
                NameNormalized = "catalog-binding",
            }
        );
        await db.SaveChangesAsync();
        CommandService commands = new(
            db,
            Substitute.For<IChannelRegistry>(),
            new RecordingEventBus(),
            Billing.TestQuota.Unlimited(),
            new TemplateHelperValidator(),
            new CommandBindingGuard([new CatalogItemBindingUsageSource(db)])
        );
        CommandDto created = (
            await commands.CreateAsync(
                Channel.ToString(),
                new CreateCommandDto { Name = "hydrate", TemplateResponse = "drink water" }
            )
        ).Value;
        CatalogItem bound = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            Name = "Hydrate",
            NameNormalized = "hydrate",
            SinkType = "pipeline",
        };
        bound.SetBindingTarget(new(BindingTargetKind.Command, created.Id.ToString()));
        db.CatalogItems.Add(bound);
        db.CatalogItems.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Channel,
                Name = "Unrelated",
                NameNormalized = "unrelated",
                SinkType = "pipeline",
            }
        );
        await db.SaveChangesAsync();

        Result result = await commands.DeleteAsync(Channel.ToString(), "hydrate", "actor");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("COMMAND_IN_USE");
        result
            .ErrorData.Should()
            .BeOfType<CommandInUse>()
            .Which.Usages.Should()
            .Equal(new BindingUsage("catalog-item", bound.Id.ToString(), "Hydrate"));
        (await db.Commands.CountAsync(c => c.Id == created.Id)).Should().Be(1);
    }
}
