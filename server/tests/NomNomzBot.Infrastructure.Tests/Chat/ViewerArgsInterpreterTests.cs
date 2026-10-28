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
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves that the text a viewer types after a command is interpreted once, at input: the whole <c>{args}</c> text
/// becomes the resolved string every later action uses, while the single words <c>{args.1}</c> stay as typed.
/// </summary>
public sealed class ViewerArgsInterpreterTests
{
    private static readonly Guid Channel = Guid.Parse("0192c000-0000-7000-8000-00000000a001");

    private static ChatMessageReceivedEvent Chat() =>
        new()
        {
            BroadcasterId = Channel,
            MessageId = "m1",
            TwitchBroadcasterId = "chan-ext",
            UserId = "viewer-9",
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = "!tts {text.repeat:3|ha}",
            Fragments = [],
            Badges = [],
            IsSubscriber = true,
            IsVip = false,
            IsModerator = false,
            IsBroadcaster = false,
        };

    [Fact]
    public async Task The_whole_args_text_becomes_the_resolved_string_and_the_words_stay_as_typed()
    {
        IViewerInputTemplateService templates = Substitute.For<IViewerInputTemplateService>();
        templates
            .ResolveRewardInputAsync(
                Channel,
                Arg.Is<ViewerInputIdentity>(v =>
                    v.TwitchUserId == "viewer-9" && v.Login == "viewer" && v.DisplayName == "Viewer"
                ),
                "{text.repeat:3|ha}",
                false,
                true,
                Arg.Any<CancellationToken>()
            )
            .Returns("hahaha");
        Dictionary<string, string> variables = ChatMessageHandler.BuildArgumentVariables(
            "{text.repeat:3|ha}"
        );

        await ViewerArgsInterpreter.InterpretAsync(templates, Chat(), variables, default);

        variables["args"].Should().Be("hahaha");
        variables["args.1"].Should().Be("{text.repeat:3|ha}");
    }

    [Fact]
    public async Task A_command_without_text_never_asks_the_service()
    {
        IViewerInputTemplateService templates = Substitute.For<IViewerInputTemplateService>();
        Dictionary<string, string> variables = ChatMessageHandler.BuildArgumentVariables("");

        await ViewerArgsInterpreter.InterpretAsync(templates, Chat(), variables, default);

        variables["args"].Should().BeEmpty();
        await templates
            .DidNotReceiveWithAnyArgs()
            .ResolveRewardInputAsync(default, default!, default!, default, default);
    }
}
