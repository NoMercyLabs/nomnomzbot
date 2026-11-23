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
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Commands.Enums;
using NomNomzBot.Domain.Commands.ValueObjects;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Stream.PipelineActions;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using NSubstitute.Core;

namespace NomNomzBot.Infrastructure.Tests.Stream.PipelineActions;

/// <summary>
/// The channel's shoutout response can run one target (a pipeline, an action or a command) instead of posting its
/// reply text, like an event response. A channel with no target keeps posting its text.
/// </summary>
public sealed class ShoutoutTargetTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000b4d1");
    private const string TargetId = "123456";

    private sealed record Rig(
        ShoutoutAction Action,
        ITwitchChatApi Chat,
        ICommandInvoker Invoker,
        ITtsDispatchService Tts
    );

    private static TwitchUser User() =>
        new(
            Id: TargetId,
            Login: "numerictarget",
            DisplayName: "NumericTarget",
            Type: "",
            BroadcasterType: "",
            Description: "",
            ProfileImageUrl: "",
            OfflineImageUrl: "",
            ViewCount: 0,
            CreatedAt: DateTimeOffset.UnixEpoch
        );

    private static async Task<Rig> BuildAsync(BindingTarget? target)
    {
        ITwitchChatApi chat = Substitute.For<ITwitchChatApi>();
        chat.SendShoutoutAsync(Channel, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        chat.SendAnnouncementAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());
        ITwitchUsersApi users = Substitute.For<ITwitchUsersApi>();
        users
            .GetUsersByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<TwitchUser>>([User()]));
        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();
        ICommandInvoker invoker = Substitute.For<ICommandInvoker>();
        invoker
            .InvokeAsync(
                Arg.Any<Guid>(),
                Arg.Any<BindingTarget>(),
                Arg.Any<InvocationContext>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success(new InvocationOutcome(true, null, null)));

        AuthDbContext db = AuthTestBuilder.NewContext();
        NomNomzBot.Domain.Identity.Entities.Channel row = new()
        {
            Id = Channel,
            Name = "stoney",
            NameNormalized = "stoney",
            OwnerUserId = Guid.NewGuid(),
            ShoutoutTemplate = "Go follow {target.displayname}!",
        };
        row.SetBindingTarget(target);
        db.Channels.Add(row);
        await db.SaveChangesAsync();

        ShoutoutAction action = ShoutoutTestFactory.Create(
            chat,
            users,
            Substitute.For<IChannelRegistry>(),
            db,
            TestTemplateResolver.Create(),
            tts,
            TimeProvider.System,
            invoker: invoker
        );
        return new(action, chat, invoker, tts);
    }

    private static PipelineExecutionContext Manual() =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "tw-1",
            TriggeredByDisplayName = "Viewer",
            MessageId = "m1",
            RawMessage = "!so numerictarget",
        };

    private static ActionDefinition Step(bool tts = true) =>
        new()
        {
            Type = "shoutout",
            Parameters = new()
            {
                ["user_id"] = JsonSerializer.SerializeToElement(TargetId),
                ["tts"] = JsonSerializer.SerializeToElement(tts),
            },
        };

    public static TheoryData<BindingTarget> Targets =>
        new()
        {
            new BindingTarget(BindingTargetKind.Pipeline, "0192a000-0000-7000-8000-00000000c001"),
            new BindingTarget(BindingTargetKind.Command, "0192a000-0000-7000-8000-00000000c002"),
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
    public async Task A_shoutout_with_a_target_runs_it_with_the_shoutout_variables_and_sends_no_text(
        BindingTarget target
    )
    {
        Rig rig = await BuildAsync(target);

        ActionResult result = await rig.Action.ExecuteAsync(Manual(), Step());

        result.Succeeded.Should().BeTrue();
        await rig
            .Chat.Received(1)
            .SendShoutoutAsync(Channel, TargetId, Arg.Any<CancellationToken>());
        await rig.Chat.DidNotReceiveWithAnyArgs().SendAnnouncementAsync(default, default!, default);
        await rig.Tts.DidNotReceiveWithAnyArgs().RequestSpeakAsync(default!);

        ICall run = rig
            .Invoker.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(ICommandInvoker.InvokeAsync));
        object?[] args = run.GetArguments();
        args[0].Should().Be(Channel);
        BindingTarget sent = (BindingTarget)args[1]!;
        sent.Kind.Should().Be(target.Kind);
        sent.Id.Should().Be(target.Id);
        sent.ActionType.Should().Be(target.ActionType);
        (sent.Params?.ToDictionary(p => p.Key, p => p.Value.GetString()))
            .Should()
            .BeEquivalentTo(target.Params?.ToDictionary(p => p.Key, p => p.Value.GetString()));
        InvocationContext context = (InvocationContext)args[2]!;
        context.TriggerSource.Should().Be("shoutout");
        context.Variables.Should().NotBeNull();
        context.Variables!["target.displayname"].Should().Be("NumericTarget");
        context.Variables["target.id"].Should().Be(TargetId);
        context.Variables["target.link"].Should().Be("twitch.tv/numerictarget");
    }

    [Fact]
    public async Task A_shoutout_with_no_target_still_sends_its_text_and_runs_nothing()
    {
        Rig rig = await BuildAsync(null);

        ActionResult result = await rig.Action.ExecuteAsync(Manual(), Step(tts: false));

        result.Succeeded.Should().BeTrue();
        await rig
            .Chat.Received(1)
            .SendAnnouncementAsync(
                Channel,
                "Go follow NumericTarget!",
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            );
        await rig.Invoker.DidNotReceiveWithAnyArgs().InvokeAsync(default, default!, default!);
    }
}
