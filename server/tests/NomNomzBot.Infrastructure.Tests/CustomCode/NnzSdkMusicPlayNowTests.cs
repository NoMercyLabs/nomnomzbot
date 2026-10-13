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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomCode.Jint;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// <c>nnz.api.music.playNow(uri)</c> plays a song now and skips the song-request queue. It is a wrapper over the
/// generic <c>play_track_once</c> action, so it carries the action's resume bookkeeping: the interrupted track
/// is remembered and comes back afterwards.
/// </summary>
public sealed class NnzSdkMusicPlayNowTests
{
    private const string ActionKey = "actions.invoke:play_track_once";
    private const string Uri = "spotify:track:intro-song";
    private const string PriorUri = "spotify:track:prior-song";
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000ac0f1");

    private sealed class ActionBridge(IOwnerActionService actions) : IScriptHostBridge
    {
        public List<(string Key, IReadOnlyList<string> Args)> Calls { get; } = [];

        public HostImportDelegate Resolve(string capabilityKey) =>
            (key, args, ct) =>
            {
                Calls.Add((key, args));
                return new ScriptActionInvoker(Channel, actions)
                    .Invoke(ScriptActionInvoker.ActionTypeOf(key)!, args, ct)
                    .ResultJson;
            };
    }

    private static ScriptCapabilityGrant Grant(params string[] keys) =>
        new(
            Guid.NewGuid(),
            [.. keys.Select(k => new ScriptCapabilityDescriptor(k, "tos", "ff", true))]
        );

    private static async Task<ScriptExecutionOutcomeResult> Run(
        string js,
        ScriptCapabilityGrant grant,
        IScriptHostBridge bridge
    ) =>
        (
            await new JintScriptExecutor().ExecuteAsync(
                new(
                    "exec-1",
                    js,
                    "hash",
                    new("u1", "User", [], new Dictionary<string, string>()),
                    ScriptResourceBudget.Baseline with
                    {
                        WallClockMs = 30_000,
                    }
                ),
                grant,
                bridge
            )
        ).Value;

    private static IOwnerActionService ActionsOver(
        IMusicService music,
        PlayOnceResumeTracker tracker
    )
    {
        MusicPlayTrackOnceAction action = new(music, tracker);
        IOwnerActionService actions = Substitute.For<IOwnerActionService>();
        actions
            .RunAsync(Arg.Any<OwnerActionRequest>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                OwnerActionRequest request = call.Arg<OwnerActionRequest>();
                ActionDefinition definition = new()
                {
                    Type = request.ActionType,
                    Parameters = request.Parameters!.ToDictionary(p => p.Key, p => p.Value),
                };
                PipelineExecutionContext context = new()
                {
                    BroadcasterId = request.BroadcasterId,
                    TriggeredByUserId = "u1",
                    TriggeredByDisplayName = "User",
                    MessageId = "m1",
                    RawMessage = "",
                };
                ActionResult result = await action.ExecuteAsync(context, definition);
                return Result.Success(
                    new WidgetActionOutcome(
                        result.Succeeded,
                        null,
                        result.ErrorMessage,
                        new Dictionary<string, string>()
                    )
                );
            });
        return actions;
    }

    private static IMusicService MusicPlaying(Result playResult)
    {
        IMusicService music = Substitute.For<IMusicService>();
        music
            .GetNowPlayingAsync(Channel.ToString(), Arg.Any<CancellationToken>())
            .Returns(
                new NowPlaying(
                    "Prior",
                    "Artist",
                    "Album",
                    null,
                    200_000,
                    42_000,
                    true,
                    100,
                    null,
                    "spotify",
                    PriorUri
                )
            );
        music
            .PlayTrackOnceAsync(Channel.ToString(), Uri, Arg.Any<CancellationToken>())
            .Returns(playResult);
        return music;
    }

    [Fact]
    public async Task PlayNow_runs_play_track_once_remembers_the_interrupted_track_and_skips_the_queue()
    {
        IMusicService music = MusicPlaying(Result.Success());
        PlayOnceResumeTracker tracker = new();
        ActionBridge bridge = new(ActionsOver(music, tracker));

        ScriptExecutionOutcomeResult r = await Run(
            $"bot.setVar('ok', String(nnz.api.music.playNow('{Uri}')));",
            Grant(ActionKey),
            bridge
        );

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        r.VariablesOut["ok"].Should().Be("true");
        bridge.Calls.Should().ContainSingle();
        bridge.Calls[0].Key.Should().Be(ActionKey);
        JsonDocument
            .Parse(bridge.Calls[0].Args[0])
            .RootElement.GetProperty("track_uri")
            .GetString()
            .Should()
            .Be(Uri);
        await music
            .Received(1)
            .PlayTrackOnceAsync(Channel.ToString(), Uri, Arg.Any<CancellationToken>());
        await music.DidNotReceiveWithAnyArgs().RequestTrackAsync(default!, default!);
        tracker.TryPeek(Channel, out PlayOnceResumeState pending).Should().BeTrue();
        pending.InterruptingTrackUri.Should().Be(Uri);
        pending.PriorTrackUri.Should().Be(PriorUri);
        pending.PriorProgressMs.Should().Be(42_000);
    }

    [Fact]
    public async Task PlayNow_returns_false_when_the_action_fails()
    {
        IMusicService music = MusicPlaying(Result.Failure("No provider.", "SERVICE_UNAVAILABLE"));
        ActionBridge bridge = new(ActionsOver(music, new PlayOnceResumeTracker()));

        ScriptExecutionOutcomeResult r = await Run(
            $"bot.setVar('ok', String(nnz.api.music.playNow('{Uri}')));",
            Grant(ActionKey),
            bridge
        );

        r.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        r.VariablesOut["ok"].Should().Be("false");
    }

    [Fact]
    public async Task PlayNow_without_the_action_grant_is_denied_and_plays_nothing()
    {
        IMusicService music = MusicPlaying(Result.Success());
        ActionBridge bridge = new(ActionsOver(music, new PlayOnceResumeTracker()));

        ScriptExecutionOutcomeResult r = await Run(
            $"nnz.api.music.playNow('{Uri}');",
            Grant("music.queue"),
            bridge
        );

        r.Outcome.Should().Be(ScriptExecutionOutcome.Denied);
        bridge.Calls.Should().BeEmpty();
        await music.DidNotReceiveWithAnyArgs().PlayTrackOnceAsync(default!, default!);
    }

    [Fact]
    public async Task Compile_declares_the_play_track_once_grant_for_playNow()
    {
        ScriptCompilation compilation = (
            await new JintScriptExecutor().CompileAsync($"nnz.api.music.playNow('{Uri}');")
        ).Value;

        compilation.DeclaredCapabilities.Should().BeEquivalentTo(ActionKey);
    }
}
