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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.RateLimiting;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Platform.Templating;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// A chat trigger binds a command (or action) directly: a matching line runs it through
/// <see cref="ICommandInvoker"/> with the sender as the actor and no pipeline, the command guard lists the
/// trigger as surface "chat-trigger", and create/update refuse a command of another channel.
/// </summary>
public sealed class ChatTriggerDirectBindingTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f401");
    private static readonly Guid OtherChannel = Guid.Parse("0192a000-0000-7000-8000-00000000f402");
    private static readonly Guid CommandId = Guid.Parse("0192a000-0000-7000-8000-00000000f403");

    private static ChatMessageReceivedEvent Line(string message) =>
        new()
        {
            BroadcasterId = Channel,
            MessageId = "msg-1",
            TwitchBroadcasterId = "tw-1",
            UserId = "tw-viewer-1",
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = message,
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

    private static (
        ChatMessageHandler Sut,
        ICommandInvoker Invoker,
        IPipelineEngine Pipeline,
        IInboundOriginChatSender Chat
    ) Build(CachedChatTrigger trigger)
    {
        ChannelContext ctx = new()
        {
            BroadcasterId = Channel,
            TwitchChannelId = "tw-1",
            ChannelName = "stoney_eagle",
        };
        ctx.ChatTriggers[trigger.Id] = trigger;
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Channel).Returns(ctx);

        ICommandInvoker invoker = Substitute.For<ICommandInvoker>();
        invoker
            .InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new InvocationOutcome(true, "drink water", null)));
        ServiceProvider provider = new ServiceCollection()
            .AddSingleton(invoker)
            .BuildServiceProvider();

        IBuiltinCommandCatalog builtins = Substitute.For<IBuiltinCommandCatalog>();
        builtins.Get(Arg.Any<string>()).Returns((IBuiltinCommand?)null);
        IInboundOriginChatSender chat = Substitute.For<IInboundOriginChatSender>();
        IPipelineEngine pipeline = Substitute.For<IPipelineEngine>();

        ChatMessageHandler sut = new(
            registry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            new CooldownManager(TimeProvider.System),
            TestReplyDispatcher.Over(chat),
            pipeline,
            builtins,
            Substitute.For<ITemplateResolver>(),
            Substitute.For<IEventBus>(),
            new(),
            TimeProvider.System,
            new OutboundSanctionAccessor(),
            TestBuiltinComposer.Create(),
            NullLogger<ChatMessageHandler>.Instance
        );
        return (sut, invoker, pipeline, chat);
    }

    private static CachedChatTrigger Trigger(BindingTarget target) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Pattern = "thirsty",
            MatchType = "contains",
            CaseSensitive = false,
            CooldownSeconds = 0,
            MinPermissionLevel = 0,
            Target = target,
        };

    [Fact]
    public async Task A_matching_line_runs_the_bound_command_as_the_sender_with_no_pipeline()
    {
        (
            ChatMessageHandler sut,
            ICommandInvoker invoker,
            IPipelineEngine pipeline,
            IInboundOriginChatSender chat
        ) = Build(Trigger(new(BindingTargetKind.Command, CommandId.ToString())));

        await sut.HandleAsync(Line("so thirsty today"), CancellationToken.None);

        await invoker
            .Received(1)
            .InvokeAsync(
                Channel,
                Arg.Is<BindingTarget>(t =>
                    t.Kind == BindingTargetKind.Command && t.Id == CommandId.ToString()
                ),
                Arg.Is<InvocationContext>(c =>
                    c.UserId == "tw-viewer-1"
                    && c.DisplayName == "Viewer"
                    && c.TriggerSource == "chat-trigger"
                    && c.Variables != null
                    && c.Variables["user.role"] == "viewer"
                    && c.Args == "so thirsty today"
                ),
                Arg.Any<CancellationToken>()
            );
        await pipeline.DidNotReceiveWithAnyArgs().ExecuteAsync(default!);
        await chat.DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task A_matching_line_runs_the_bound_action_with_its_params()
    {
        (ChatMessageHandler sut, ICommandInvoker invoker, _, _) = Build(
            Trigger(
                new(
                    BindingTargetKind.Action,
                    ActionType: "play_sound",
                    Params: new Dictionary<string, System.Text.Json.JsonElement>
                    {
                        ["clip"] = System.Text.Json.JsonDocument.Parse("\"airhorn\"").RootElement,
                    }
                )
            )
        );

        await sut.HandleAsync(Line("thirsty"), CancellationToken.None);

        await invoker
            .Received(1)
            .InvokeAsync(
                Channel,
                Arg.Is<BindingTarget>(t =>
                    t.Kind == BindingTargetKind.Action
                    && t.ActionType == "play_sound"
                    && t.Params!["clip"].GetString() == "airhorn"
                ),
                Arg.Is<InvocationContext>(c => c.TriggerSource == "chat-trigger"),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_line_that_does_not_match_never_reaches_the_invoker()
    {
        (ChatMessageHandler sut, ICommandInvoker invoker, _, _) = Build(
            Trigger(new(BindingTargetKind.Command, CommandId.ToString()))
        );

        await sut.HandleAsync(Line("all good here"), CancellationToken.None);

        await invoker.DidNotReceiveWithAnyArgs().InvokeAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Deleting_a_command_a_chat_trigger_runs_is_refused_and_lists_the_trigger()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Channels.Add(
            new()
            {
                Id = Channel,
                OwnerUserId = Channel,
                Name = "trigger-binding",
                NameNormalized = "trigger-binding",
            }
        );
        await db.SaveChangesAsync();
        CommandService commands = new(
            db,
            Substitute.For<IChannelRegistry>(),
            new RecordingEventBus(),
            Billing.TestQuota.Unlimited(),
            new TemplateHelperValidator(),
            new CommandBindingGuard([new ChatTriggerBindingUsageSource(db)])
        );
        CommandDto created = (
            await commands.CreateAsync(
                Channel.ToString(),
                new CreateCommandDto { Name = "hydrate", TemplateResponse = "drink water" }
            )
        ).Value;
        ChatTrigger bound = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = Channel,
            Pattern = "thirsty",
        };
        bound.SetBindingTarget(new(BindingTargetKind.Command, created.Id.ToString()));
        db.ChatTriggers.Add(bound);
        db.ChatTriggers.Add(
            new()
            {
                Id = Guid.CreateVersion7(),
                BroadcasterId = Channel,
                Pattern = "unrelated",
                Response = "x",
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
            .Equal(new BindingUsage("chat-trigger", bound.Id.ToString(), "thirsty"));
    }

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
        ChatTriggerService sut = new(db, Substitute.For<IChannelRegistry>());

        Result<ChatTriggerDto> result = await sut.CreateAsync(
            Channel.ToString(),
            new()
            {
                Pattern = "thirsty",
                Response = "a text reply that would be valid on its own",
                Target = new(BindingTargetKind.Command, foreignCommand.ToString()),
            }
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        db.ChatTriggers.Count().Should().Be(0);
    }

    [Fact]
    public async Task Create_with_a_command_of_this_channel_persists_it_and_the_dto_shows_the_target()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
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
        await db.SaveChangesAsync();
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        ChatTriggerService sut = new(db, registry);

        Result<ChatTriggerDto> result = await sut.CreateAsync(
            Channel.ToString(),
            new()
            {
                Pattern = "thirsty",
                Target = new(BindingTargetKind.Command, CommandId.ToString()),
            }
        );

        result.IsSuccess.Should().BeTrue();
        result
            .Value!.Target.Should()
            .Be(new BindingTarget(BindingTargetKind.Command, CommandId.ToString()));
        ChatTrigger stored = db.ChatTriggers.Single();
        stored.TargetKind.Should().Be(BindingTargetKind.Command);
        stored.TargetCommandId.Should().Be(CommandId);
        stored.PipelineId.Should().BeNull();
        stored.Response.Should().BeNull();
        await registry
            .Received(1)
            .InvalidateChatTriggersAsync(Channel, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Update_to_an_action_target_stores_the_action_and_clears_the_pipeline()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        Guid triggerId = Guid.CreateVersion7();
        db.ChatTriggers.Add(
            new()
            {
                Id = triggerId,
                BroadcasterId = Channel,
                Pattern = "thirsty",
                Response = "hi",
            }
        );
        await db.SaveChangesAsync();
        ChatTriggerService sut = new(db, Substitute.For<IChannelRegistry>());

        Result<ChatTriggerDto> result = await sut.UpdateAsync(
            Channel.ToString(),
            triggerId,
            new() { Target = new(BindingTargetKind.Action, ActionType: "play_sound") }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value!.Target!.Kind.Should().Be(BindingTargetKind.Action);
        result.Value.Target.ActionType.Should().Be("play_sound");
        ChatTrigger stored = db.ChatTriggers.Single();
        stored.TargetKind.Should().Be(BindingTargetKind.Action);
        stored.TargetActionType.Should().Be("play_sound");
        stored.PipelineId.Should().BeNull();
    }
}
