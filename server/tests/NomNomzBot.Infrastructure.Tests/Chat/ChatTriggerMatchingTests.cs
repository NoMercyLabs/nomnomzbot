// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.RateLimiting;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Chat.EventHandlers;
using NomNomzBot.Infrastructure.Platform.RateLimiting;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves the keyword chat-trigger hot path: an ordinary line matching a cached trigger sends the
/// RESOLVED template (or runs the bound pipeline with the speaker's variables); matching is
/// case-insensitive by default and honors exact/starts_with/regex; the per-trigger cooldown suppresses
/// spam; a role floor blocks low-role speakers; only the FIRST match fires; and command lines (<c>!</c>)
/// never reach the trigger surface.
/// </summary>
public sealed class ChatTriggerMatchingTests
{
    private static readonly Guid Broadcaster = Guid.Parse("019f6d00-5555-7000-8000-000000000001");

    private static ChannelContext NewContext() =>
        new()
        {
            BroadcasterId = Broadcaster,
            TwitchChannelId = "tw-1",
            ChannelName = "stoney_eagle",
        };

    private static CachedChatTrigger Trigger(
        string pattern,
        string matchType = "contains",
        string? response = "hi {user}",
        string? pipelineJson = null,
        int cooldownSeconds = 0,
        int minLevel = 0,
        bool caseSensitive = false
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            Pattern = pattern,
            MatchType = matchType,
            CaseSensitive = caseSensitive,
            Response = response,
            PipelineGraphJson = pipelineJson,
            CooldownSeconds = cooldownSeconds,
            MinPermissionLevel = minLevel,
            CompiledRegex =
                matchType == "regex"
                    ? new Regex(
                        pattern,
                        caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase,
                        TimeSpan.FromMilliseconds(100)
                    )
                    : null,
        };

    private static (ChatMessageHandler Sut, IChatProvider Chat, IPipelineEngine Pipeline) Build(
        ChannelContext ctx
    )
    {
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Broadcaster).Returns(ctx);

        ITemplateResolver templates = Substitute.For<ITemplateResolver>();
        templates
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(callInfo => Task.FromResult($"resolved:{callInfo.ArgAt<string>(0)}"));

        IChatProvider chat = Substitute.For<IChatProvider>();
        IPipelineEngine pipeline = Substitute.For<IPipelineEngine>();

        // An empty builtin catalog: NSubstitute would otherwise auto-return a substitute builtin,
        // sending a command line down a half-mocked execution path instead of the real "unknown" one.
        IBuiltinCommandCatalog builtins = Substitute.For<IBuiltinCommandCatalog>();
        builtins.Get(Arg.Any<string>()).Returns((IBuiltinCommand?)null);

        ChatMessageHandler sut = new(
            registry,
            Substitute.For<IServiceScopeFactory>(),
            new CooldownManager(TimeProvider.System), // real — cooldown behavior is part of what we prove
            chat,
            pipeline,
            builtins,
            templates,
            Substitute.For<IEventBus>(),
            TimeProvider.System,
            NullLogger<ChatMessageHandler>.Instance
        );
        return (sut, chat, pipeline);
    }

    private static ChatMessageReceivedEvent Line(
        string message,
        bool isModerator = false,
        string userId = "tw-viewer-1"
    ) =>
        new()
        {
            BroadcasterId = Broadcaster,
            MessageId = "msg-1",
            TwitchBroadcasterId = "tw-1",
            UserId = userId,
            UserDisplayName = "Viewer",
            UserLogin = "viewer",
            Message = message,
            Fragments = [],
            Badges = [],
            IsSubscriber = false,
            IsVip = false,
            IsModerator = isModerator,
            IsBroadcaster = false,
        };

    [Fact]
    public async Task A_containing_line_sends_the_resolved_template_case_insensitively()
    {
        ChannelContext ctx = NewContext();
        CachedChatTrigger trigger = Trigger("good bot");
        ctx.ChatTriggers[trigger.Id] = trigger;
        (ChatMessageHandler sut, IChatProvider chat, _) = Build(ctx);

        await sut.HandleAsync(Line("such a GOOD BOT today"), CancellationToken.None);

        await chat.Received(1)
            .SendMessageAsync(Broadcaster, "resolved:hi {user}", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("exact", "good bot", "good bot", true)]
    [InlineData("exact", "good bot", "such a good bot", false)]
    [InlineData("starts_with", "hello", "hello everyone", true)]
    [InlineData("starts_with", "hello", "well hello", false)]
    [InlineData("regex", @"\bpog(gers)?\b", "that was POGGERS", true)]
    [InlineData("regex", @"\bpog(gers)?\b", "pogNOTaword", false)]
    public async Task Match_types_behave_as_documented(
        string matchType,
        string pattern,
        string message,
        bool shouldFire
    )
    {
        ChannelContext ctx = NewContext();
        CachedChatTrigger trigger = Trigger(pattern, matchType);
        ctx.ChatTriggers[trigger.Id] = trigger;
        (ChatMessageHandler sut, IChatProvider chat, _) = Build(ctx);

        await sut.HandleAsync(Line(message), CancellationToken.None);

        await chat.Received(shouldFire ? 1 : 0)
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_cooldown_suppresses_a_second_fire()
    {
        ChannelContext ctx = NewContext();
        CachedChatTrigger trigger = Trigger("hype", cooldownSeconds: 300);
        ctx.ChatTriggers[trigger.Id] = trigger;
        (ChatMessageHandler sut, IChatProvider chat, _) = Build(ctx);

        await sut.HandleAsync(Line("hype!"), CancellationToken.None);
        await sut.HandleAsync(Line("hype again!"), CancellationToken.None);

        await chat.Received(1)
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_role_floor_blocks_low_role_speakers_but_not_clearing_ones()
    {
        ChannelContext ctx = NewContext();
        CachedChatTrigger trigger = Trigger("secret", minLevel: 10); // moderator floor
        ctx.ChatTriggers[trigger.Id] = trigger;
        (ChatMessageHandler sut, IChatProvider chat, _) = Build(ctx);

        await sut.HandleAsync(Line("the secret word"), CancellationToken.None);
        await chat.DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());

        await sut.HandleAsync(Line("the secret word", isModerator: true), CancellationToken.None);
        await chat.Received(1)
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_bound_pipeline_runs_with_the_speakers_variables_instead_of_a_chat_line()
    {
        ChannelContext ctx = NewContext();
        CachedChatTrigger trigger = Trigger(
            "welcome chain",
            response: null,
            pipelineJson: """{"steps":[]}"""
        );
        ctx.ChatTriggers[trigger.Id] = trigger;
        (ChatMessageHandler sut, IChatProvider chat, IPipelineEngine pipeline) = Build(ctx);

        await sut.HandleAsync(Line("welcome chain go"), CancellationToken.None);

        await pipeline
            .Received(1)
            .ExecuteAsync(
                Arg.Is<PipelineRequest>(r =>
                    r.BroadcasterId == Broadcaster
                    && r.PipelineJson == """{"steps":[]}"""
                    && r.TriggeredByUserId == "tw-viewer-1"
                    && r.InitialVariables!["user"] == "Viewer"
                ),
                Arg.Any<CancellationToken>()
            );
        await chat.DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Only_the_first_matching_trigger_fires()
    {
        ChannelContext ctx = NewContext();
        CachedChatTrigger first = Trigger("hello");
        CachedChatTrigger second = Trigger("hello there");
        ctx.ChatTriggers[first.Id] = first;
        ctx.ChatTriggers[second.Id] = second;
        (ChatMessageHandler sut, IChatProvider chat, _) = Build(ctx);

        await sut.HandleAsync(Line("hello there friends"), CancellationToken.None);

        await chat.Received(1)
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_command_line_never_reaches_the_trigger_surface()
    {
        ChannelContext ctx = NewContext();
        CachedChatTrigger trigger = Trigger("!uptime");
        ctx.ChatTriggers[trigger.Id] = trigger;
        (ChatMessageHandler sut, IChatProvider chat, _) = Build(ctx);

        await sut.HandleAsync(Line("!uptime"), CancellationToken.None);

        await chat.DidNotReceiveWithAnyArgs()
            .SendMessageAsync(default, default!, Arg.Any<CancellationToken>());
    }
}
