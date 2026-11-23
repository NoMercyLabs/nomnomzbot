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

using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Chat;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Infrastructure.Moderation.EventHandlers;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Platform.Security;
using NSubstitute;
using NSubstitute.Core;

namespace NomNomzBot.Infrastructure.Tests.Moderation;

/// <summary>
/// A chat filter keeps its moderation action and can also run one target (a pipeline, an action or a command)
/// when it matches. The target runs after the moderation action, with the match variables. A filter with no
/// target behaves as before.
/// </summary>
public sealed class ChatFilterTargetTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000000f2");
    private const string ViewerId = "viewer-777";
    private static readonly DateTimeOffset T0 = new(2026, 11, 23, 7, 0, 0, TimeSpan.Zero);

    private sealed record Rig(
        ChatFilterExecutionHandler Handler,
        EventStoreTestDbContext Db,
        ITwitchModerationApi Moderation,
        ICommandInvoker Invoker
    );

    private static Rig Build(bool deleteSucceeds = true)
    {
        SqliteTestDatabase database = SqliteTestDatabase.Open();
        EventStoreTestDbContext db = database.NewContext();
        ITwitchModerationApi moderation = Substitute.For<ITwitchModerationApi>();
        moderation
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                deleteSucceeds
                    ? Result.Success()
                    : Result.Failure("refused", "PLATFORM_ACTION_FAILED")
            );
        moderation
            .TimeoutUserAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new TwitchBanResult("tw-chan", "mod", ViewerId, T0, null)));

        ICommandInvoker invoker = Substitute.For<ICommandInvoker>();
        invoker
            .InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new InvocationOutcome(true, null, null)));

        ChatFilterExecutionHandler handler = new(
            db,
            moderation,
            Substitute.For<IInboundOriginModerator>(),
            Substitute.For<IModerationEscalationService>(),
            Substitute.For<IModerationQueueService>(),
            Substitute.For<IUserService>(),
            TestSanction.Held(),
            new RecordingEventBus(),
            invoker,
            NullLogger<ChatFilterExecutionHandler>.Instance
        );
        return new(handler, db, moderation, invoker);
    }

    private static ChatMessageReceivedEvent Message(string text) =>
        new()
        {
            MessageId = "msg-9",
            BroadcasterId = Channel,
            TwitchBroadcasterId = "tw-chan",
            UserId = ViewerId,
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = text,
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

    private static async Task<ChatFilter> SeedAsync(
        EventStoreTestDbContext db,
        ChatFilterAction action,
        BindingTarget? target
    )
    {
        ChatFilter filter = new()
        {
            BroadcasterId = Channel,
            FilterType = ChatFilterType.Blocklist,
            Name = "no-spoilers",
            Action = action,
            TermsJson = JsonSerializer.Serialize(new List<string> { "spoiler" }),
            TimeoutSeconds = action == ChatFilterAction.Timeout ? 30 : null,
            IsEnabled = true,
        };
        filter.SetBindingTarget(target);
        db.ChatFilters.Add(filter);
        await db.SaveChangesAsync();
        return filter;
    }

    public static TheoryData<BindingTarget> Targets =>
        new()
        {
            new BindingTarget(BindingTargetKind.Pipeline, "0192a000-0000-7000-8000-00000000d001"),
            new BindingTarget(BindingTargetKind.Command, "0192a000-0000-7000-8000-00000000d002"),
            new BindingTarget(
                BindingTargetKind.Action,
                ActionType: "play_sound",
                Params: new Dictionary<string, JsonElement>
                {
                    ["sound"] = JsonSerializer.SerializeToElement("fanfare"),
                }
            ),
        };

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task A_matching_filter_deletes_the_message_and_runs_its_target_with_the_match_variables(
        BindingTarget target
    )
    {
        Rig rig = Build();
        await SeedAsync(rig.Db, ChatFilterAction.Delete, target);

        await rig.Handler.HandleAsync(Message("a big spoiler here"));

        await rig
            .Moderation.Received(1)
            .DeleteChatMessageAsync(Channel, "msg-9", Arg.Any<CancellationToken>());

        ICall run = rig
            .Invoker.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(ICommandInvoker.InvokeAsync));
        object?[] args = run.GetArguments();
        args[0].Should().Be(Channel);
        BindingTarget sent = (BindingTarget)args[1]!;
        sent.Kind.Should().Be(target.Kind);
        sent.Id.Should().Be(target.Id);
        sent.ActionType.Should().Be(target.ActionType);
        InvocationContext context = (InvocationContext)args[2]!;
        context.TriggerSource.Should().Be("chat_filter");
        context.UserId.Should().Be(ViewerId);
        context.DisplayName.Should().Be("Viewer");
        context.Variables.Should().NotBeNull();
        context.Variables!["message"].Should().Be("a big spoiler here");
        context.Variables["filter.name"].Should().Be("no-spoilers");
        context.Variables["user"].Should().Be("viewer");
    }

    [Fact]
    public async Task The_target_runs_after_the_moderation_action_even_when_the_action_is_a_timeout()
    {
        Rig rig = Build();
        await SeedAsync(
            rig.Db,
            ChatFilterAction.Timeout,
            new(BindingTargetKind.Command, "0192a000-0000-7000-8000-00000000d002")
        );

        await rig.Handler.HandleAsync(Message("spoiler"));

        await rig
            .Moderation.Received(1)
            .TimeoutUserAsync(
                Channel,
                ViewerId,
                30,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        rig.Invoker.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(ICommandInvoker.InvokeAsync))
            .Should()
            .Be(1);
        (await rig.Db.ChatFilters.SingleAsync()).MatchCount.Should().Be(1);
    }

    [Fact]
    public async Task A_filter_with_no_target_moderates_as_before_and_runs_nothing()
    {
        Rig rig = Build();
        await SeedAsync(rig.Db, ChatFilterAction.Delete, null);

        await rig.Handler.HandleAsync(Message("a spoiler"));

        await rig
            .Moderation.Received(1)
            .DeleteChatMessageAsync(Channel, "msg-9", Arg.Any<CancellationToken>());
        await rig
            .Invoker.DidNotReceive()
            .InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            );
        (await rig.Db.ChatFilters.SingleAsync()).MatchCount.Should().Be(1);
    }

    [Fact]
    public async Task A_message_that_matches_nothing_runs_no_target()
    {
        Rig rig = Build();
        await SeedAsync(
            rig.Db,
            ChatFilterAction.Delete,
            new(BindingTargetKind.Command, "0192a000-0000-7000-8000-00000000d002")
        );

        await rig.Handler.HandleAsync(Message("a harmless line"));

        await rig
            .Invoker.DidNotReceive()
            .InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task A_line_the_bot_itself_stamped_never_matches_so_a_target_cannot_loop_back()
    {
        Rig rig = Build();
        await SeedAsync(
            rig.Db,
            ChatFilterAction.Delete,
            new(BindingTargetKind.Command, "0192a000-0000-7000-8000-00000000d002")
        );

        await rig.Handler.HandleAsync(Message(BotEmittedLine.Stamp("a spoiler from the bot")));

        await rig
            .Moderation.DidNotReceive()
            .DeleteChatMessageAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await rig
            .Invoker.DidNotReceive()
            .InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            );
    }
}
