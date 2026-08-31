// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Chat.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat.PipelineActions;

/// <summary>
/// The <c>announce</c> pipeline action must call the real Helix "Send Chat Announcement" method
/// (<see cref="ITwitchChatApi.SendAnnouncementAsync"/>) — never plain <c>SendMessageAsync</c> — with the
/// tone-resolved message text, the triggering channel, and the requested (validated) highlight color.
/// </summary>
public sealed class AnnounceActionTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0198c000-0000-7000-8000-00000000f001");

    private static (AnnounceAction Action, ITwitchChatApi Chat, ITemplateResolver Resolver) Build()
    {
        ITwitchChatApi chat = Substitute.For<ITwitchChatApi>();
        chat.SendAnnouncementAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            // Naive stand-in for tone/template resolution: substitutes {name} placeholders from the
            // run's variable bag, proving the action feeds ITemplateResolver (the same resolution path
            // SendMessageAction uses) rather than posting the raw, unresolved template.
            .Returns(ci =>
            {
                string template = (string)ci[0];
                IDictionary<string, string> vars = (IDictionary<string, string>)ci[1];
                foreach (KeyValuePair<string, string> kv in vars)
                    template = template.Replace("{" + kv.Key + "}", kv.Value);
                return Task.FromResult(template);
            });

        return (new(chat, resolver), chat, resolver);
    }

    private static PipelineExecutionContext BuildContext()
    {
        PipelineExecutionContext ctx = new()
        {
            BroadcasterId = Broadcaster,
            TriggeredByUserId = "u1",
            TriggeredByDisplayName = "Viewer",
            MessageId = "msg-1",
            RawMessage = "!announce",
        };
        ctx.Variables["user.name"] = "Viewer";
        return ctx;
    }

    private static ActionDefinition Announce(string message, string? color = null)
    {
        Dictionary<string, JsonElement> parameters = new()
        {
            ["message"] = JsonSerializer.SerializeToElement(message),
        };
        if (color is not null)
            parameters["color"] = JsonSerializer.SerializeToElement(color);
        return new() { Type = "announce", Parameters = parameters };
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesTemplateAndCallsHelixAnnouncementWithColor()
    {
        (AnnounceAction action, ITwitchChatApi chat, _) = Build();

        ActionResult result = await action.ExecuteAsync(
            BuildContext(),
            Announce("gg {user.name}!", "green")
        );

        result.Succeeded.Should().BeTrue();
        await chat.Received(1)
            .SendAnnouncementAsync(
                Broadcaster,
                "gg Viewer!",
                "green",
                Arg.Any<CancellationToken>()
            );
        // Never the plain chat-send path — this is the highlighted-banner endpoint, a distinct call.
        await chat.DidNotReceiveWithAnyArgs().SendShoutoutAsync(default, default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidColor_PassesNullInsteadOfGarbageToHelix()
    {
        (AnnounceAction action, ITwitchChatApi chat, _) = Build();

        await action.ExecuteAsync(BuildContext(), Announce("hi", "not-a-real-color"));

        await chat.Received(1)
            .SendAnnouncementAsync(Broadcaster, "hi", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_MissingMessage_FailsWithoutCallingHelix()
    {
        (AnnounceAction action, ITwitchChatApi chat, _) = Build();

        ActionResult result = await action.ExecuteAsync(
            BuildContext(),
            new() { Type = "announce", Parameters = new() }
        );

        result.Succeeded.Should().BeFalse();
        await chat.DidNotReceiveWithAnyArgs()
            .SendAnnouncementAsync(default, default!, default, default);
    }

    [Fact]
    public async Task ExecuteAsync_HelixRejectsAnnouncement_ReturnsFailureWithReason()
    {
        (AnnounceAction action, ITwitchChatApi chat, _) = Build();
        chat.SendAnnouncementAsync(
                Broadcaster,
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure("missing moderator:manage:announcements scope"));

        ActionResult result = await action.ExecuteAsync(BuildContext(), Announce("hi"));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("missing moderator:manage:announcements scope");
    }
}
