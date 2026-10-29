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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Giveaways.Dtos;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Giveaways.Entities;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Giveaways;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Persistence;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Giveaways;

/// <summary>
/// A giveaway prize binds a command (or action) directly: a drawn winner's fulfillment runs it through
/// <see cref="ICommandInvoker"/> with the winner as the invoker, the command guard lists the giveaway, and
/// create/update refuse a command that is not in the channel and persist the target's shape.
/// </summary>
public sealed class GiveawayDirectBindingTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000e101");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000e102");
    private static readonly Guid CommandId = Guid.Parse("0192a000-0000-7000-8000-00000000e103");

    private static GiveawayFulfillment BuildFulfillment(
        AuthDbContext db,
        IPipelineEngine engine,
        ICommandInvoker invoker
    ) =>
        new(
            db,
            Substitute.For<ICurrencyAccountService>(),
            new ServiceCollection()
                .AddScoped(_ => engine)
                .AddScoped(_ => invoker)
                .BuildServiceProvider(),
            [],
            Substitute.For<IInboundOriginChatSender>(),
            Substitute.For<ITokenProtector>(),
            TimeProvider.System,
            NullLogger<GiveawayFulfillment>.Instance
        );

    private static ICommandInvoker SucceedingInvoker()
    {
        ICommandInvoker invoker = Substitute.For<ICommandInvoker>();
        invoker
            .InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new InvocationOutcome(true, "congrats", null)));
        return invoker;
    }

    private static (Giveaway Giveaway, GiveawayWinner Winner) SeedDraw(
        AuthDbContext db,
        BindingTarget target
    )
    {
        User viewer = new()
        {
            Id = Guid.CreateVersion7(),
            Username = "lucky",
            UsernameNormalized = "lucky",
            DisplayName = "Lucky",
            TwitchUserId = "tw-lucky",
        };
        Giveaway giveaway = new()
        {
            BroadcasterId = Channel,
            Title = "Prize Drop",
            EntryMode = GiveawayEntryMode.Keyword,
            Keyword = "!win",
            PrizeMode = GiveawayPrizeMode.Pipeline,
        };
        giveaway.SetBindingTarget(target);
        GiveawayWinner winner = new()
        {
            BroadcasterId = Channel,
            GiveawayId = giveaway.Id,
            ViewerUserId = viewer.Id,
            ViewerTwitchUserId = "tw-lucky",
            ProviderUserId = "tw-lucky",
            DrawnAt = DateTime.UtcNow,
        };
        db.Users.Add(viewer);
        db.Giveaways.Add(giveaway);
        db.GiveawayWinners.Add(winner);
        db.SaveChanges();
        return (giveaway, winner);
    }

    [Fact]
    public async Task A_prize_bound_to_a_command_runs_that_command_as_the_winner_with_no_pipeline()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        ICommandInvoker invoker = SucceedingInvoker();
        GiveawayFulfillment sut = BuildFulfillment(db, engine, invoker);
        (Giveaway giveaway, GiveawayWinner winner) = SeedDraw(
            db,
            new(BindingTargetKind.Command, CommandId.ToString())
        );

        await sut.FulfillAsync(giveaway, winner);

        await invoker
            .Received(1)
            .InvokeAsync(
                Channel,
                Arg.Is<BindingTarget>(t =>
                    t.Kind == BindingTargetKind.Command && t.Id == CommandId.ToString()
                ),
                Arg.Is<InvocationContext>(c =>
                    c.UserId == "tw-lucky"
                    && c.DisplayName == "Lucky"
                    && c.TriggerSource == "giveaway"
                ),
                Arg.Any<CancellationToken>()
            );
        await engine.DidNotReceiveWithAnyArgs().ExecuteAsync(default!);
    }

    [Fact]
    public async Task A_prize_bound_to_an_action_runs_that_action_with_its_params()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ICommandInvoker invoker = SucceedingInvoker();
        GiveawayFulfillment sut = BuildFulfillment(db, Substitute.For<IPipelineEngine>(), invoker);
        (Giveaway giveaway, GiveawayWinner winner) = SeedDraw(
            db,
            new(BindingTargetKind.Action, ActionType: "send_message")
        );

        await sut.FulfillAsync(giveaway, winner);

        await invoker
            .Received(1)
            .InvokeAsync(
                Channel,
                Arg.Is<BindingTarget>(t =>
                    t.Kind == BindingTargetKind.Action && t.ActionType == "send_message"
                ),
                Arg.Is<InvocationContext>(c => c.TriggerSource == "giveaway"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_prize_bound_to_a_pipeline_still_runs_the_pipeline_and_never_the_invoker()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        ICommandInvoker invoker = SucceedingInvoker();
        GiveawayFulfillment sut = BuildFulfillment(db, engine, invoker);
        Guid pipelineId = Guid.CreateVersion7();
        (Giveaway giveaway, GiveawayWinner winner) = SeedDraw(
            db,
            new(BindingTargetKind.Pipeline, pipelineId.ToString())
        );

        await sut.FulfillAsync(giveaway, winner);

        await engine
            .Received(1)
            .ExecuteAsync(
                Arg.Is<PipelineRequest>(r => r.PipelineId == pipelineId),
                Arg.Any<CancellationToken>()
            );
        await invoker.DidNotReceiveWithAnyArgs().InvokeAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Deleting_a_command_a_giveaway_prize_runs_is_refused_and_lists_the_giveaway()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Channel,
                Name = "giveaway-binding",
                NameNormalized = "giveaway-binding",
            }
        );
        await db.SaveChangesAsync();
        CommandService commands = new(
            db,
            Substitute.For<IChannelRegistry>(),
            new RecordingEventBus(),
            Billing.TestQuota.Unlimited(),
            new TemplateHelperValidator(),
            new CommandBindingGuard([new GiveawayBindingUsageSource(db)])
        );
        CommandDto created = (
            await commands.CreateAsync(
                Channel.ToString(),
                new CreateCommandDto { Name = "prize", TemplateResponse = "congrats" }
            )
        ).Value;
        Giveaway bound = new()
        {
            BroadcasterId = Channel,
            Title = "Prize Drop",
            EntryMode = GiveawayEntryMode.Keyword,
            PrizeMode = GiveawayPrizeMode.Pipeline,
        };
        bound.SetBindingTarget(new(BindingTargetKind.Command, created.Id.ToString()));
        db.Giveaways.Add(bound);
        await db.SaveChangesAsync();

        Result result = await commands.DeleteAsync(Channel.ToString(), "prize", "actor");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("COMMAND_IN_USE");
        result
            .ErrorData.Should()
            .BeOfType<CommandInUse>()
            .Which.Usages.Should()
            .Equal(new BindingUsage("giveaway", bound.Id.ToString(), "Prize Drop"));
        (await db.Commands.CountAsync(c => c.Id == created.Id)).Should().Be(1);
    }

    private static GiveawayService BuildService(AuthDbContext db) =>
        new(
            db,
            new PassThroughUnitOfWork(),
            Substitute.For<IEventBus>(),
            Substitute.For<ICurrencyAccountService>(),
            Substitute.For<IGiveawayFulfillment>(),
            Substitute.For<IAgeConsentService>(),
            TimeProvider.System,
            NullLogger<GiveawayService>.Instance
        );

    private static UpsertGiveawayRequest PrizeRequest(BindingTarget target) =>
        new(
            "Prize Drop",
            GiveawayEntryMode.Keyword,
            Keyword: "!win",
            PrizeMode: GiveawayPrizeMode.Pipeline,
            Target: target
        );

    [Fact]
    public async Task Create_with_a_command_of_another_channel_is_refused_and_nothing_is_stored()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid foreignCommand = Guid.CreateVersion7();
        db.Commands.Add(
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
        await db.SaveChangesAsync();

        Result<GiveawayDto> result = await BuildService(db)
            .CreateAsync(
                Channel,
                PrizeRequest(new(BindingTargetKind.Command, foreignCommand.ToString()))
            );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await db.Giveaways.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Create_with_a_command_of_this_channel_persists_the_target_and_the_dto_shows_it()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Commands.Add(
            new()
            {
                Id = CommandId,
                BroadcasterId = Channel,
                Name = "prize",
                NameNormalized = "prize",
                Tier = "template",
                TemplateResponse = "congrats",
            }
        );
        await db.SaveChangesAsync();

        Result<GiveawayDto> result = await BuildService(db)
            .CreateAsync(
                Channel,
                PrizeRequest(new(BindingTargetKind.Command, CommandId.ToString()))
            );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result
            .Value!.Target.Should()
            .Be(new BindingTarget(BindingTargetKind.Command, CommandId.ToString()));
        Giveaway stored = await db.Giveaways.SingleAsync();
        stored.TargetKind.Should().Be(BindingTargetKind.Command);
        stored.TargetCommandId.Should().Be(CommandId);
        stored.PrizePipelineId.Should().BeNull();
    }
}
