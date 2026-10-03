// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Infrastructure.CustomCode.Jint;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// A script that leaves out an argument must never send the text "undefined" to the host. A left-out optional
/// argument means its documented default; a left-out required argument is a clear script error that names the
/// call and the argument.
/// </summary>
public sealed class NnzSdkLeftOutArgumentTests
{
    private sealed record HostCall(string Key, IReadOnlyList<string> Args);

    private sealed class RecordingBridge : IScriptHostBridge
    {
        public List<HostCall> Calls { get; } = [];

        public HostImportDelegate Resolve(string capabilityKey) =>
            (key, args, ct) =>
            {
                Calls.Add(new(key, args));
                return null;
            };
    }

    private static readonly ScriptResourceBudget Generous = ScriptResourceBudget.Baseline with
    {
        WallClockMs = 30_000,
    };

    private static ScriptCapabilityGrant GrantAll() =>
        new(
            Guid.NewGuid(),
            [
                .. new[]
                {
                    "chat.send",
                    "chat.reply",
                    "music.queue",
                    "http.fetch",
                    "storage.get",
                    "storage.set",
                    "storage.delete",
                    "tts.speak",
                    "tts.voice.get",
                    "tts.voice.set",
                    "widget.emit",
                    "reward.get",
                    "reward.update",
                    "schedule.pipeline",
                    "actions.invoke:tts_synthesize",
                }.Select(k => new ScriptCapabilityDescriptor(k, "tos", "ff", true)),
            ]
        );

    private static async Task<(
        ScriptExecutionOutcomeResult Outcome,
        RecordingBridge Bridge,
        List<string> Sent
    )> Run(string js)
    {
        RecordingBridge bridge = new();
        List<string> sent = [];
        ScriptExecutionRequest request = new(
            "exec-1",
            js,
            "hash",
            new("u1", "User", [], new Dictionary<string, string>()),
            Generous
        )
        {
            OnBotSend = sent.Add,
        };
        ScriptExecutionOutcomeResult outcome = (
            await new JintScriptExecutor().ExecuteAsync(request, GrantAll(), bridge)
        ).Value;
        return (outcome, bridge, sent);
    }

    [Fact]
    public async Task GetVoice_with_no_argument_asks_the_host_for_the_triggering_viewer()
    {
        (ScriptExecutionOutcomeResult outcome, RecordingBridge bridge, _) = await Run(
            "nnz.api.tts.getVoice();"
        );

        outcome.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        bridge.Calls.Should().ContainSingle();
        bridge.Calls[0].Key.Should().Be("tts.voice.get");
        bridge.Calls[0].Args.Should().BeEmpty();
    }

    [Theory]
    [InlineData("nnz.api.chat.send();", "chat.send needs a message")]
    [InlineData("nnz.api.chat.reply();", "chat.reply needs a message")]
    [InlineData("nnz.api.music.queue();", "music.queue needs a track uri")]
    [InlineData("nnz.api.http.fetch();", "http.fetch needs a url")]
    [InlineData("nnz.api.storage.get();", "storage.get needs a key")]
    [InlineData("nnz.api.storage.set('k');", "storage.set needs a value")]
    [InlineData("nnz.api.storage.set(undefined, 'v');", "storage.set needs a key")]
    [InlineData("nnz.api.storage.delete();", "storage.delete needs a key")]
    [InlineData("nnz.api.tts.speak();", "tts.speak needs the text to say")]
    [InlineData("nnz.api.tts.setVoice();", "tts.setVoice needs a user id or login")]
    [InlineData("nnz.api.widget.emit('w');", "widget.emit needs an event type")]
    [InlineData("nnz.api.widget.emit(undefined, 'e');", "widget.emit needs a widget id or name")]
    [InlineData("nnz.api.reward.get();", "reward.get needs a reward id or title")]
    [InlineData("nnz.api.reward.update('r');", "reward.update needs a patch")]
    [InlineData("nnz.api.schedule.pipeline('p');", "schedule.pipeline needs a delay in seconds")]
    [InlineData(
        "nnz.api.schedule.pipeline(undefined, 5);",
        "schedule.pipeline needs a pipeline name"
    )]
    [InlineData("nnz.api.actions.invoke();", "actions.invoke needs an action type")]
    [InlineData("nnz.time.sleep();", "nnz.time.sleep needs a number of milliseconds")]
    [InlineData("nnz.time.sleep(undefined);", "nnz.time.sleep needs a number of milliseconds")]
    [InlineData("nnz.time.sleep('soon');", "nnz.time.sleep needs a number of milliseconds")]
    [InlineData("bot.send();", "bot.send needs a message")]
    [InlineData("bot.getVar();", "bot.getVar needs a key")]
    [InlineData("bot.setVar('k');", "bot.setVar needs a value")]
    [InlineData("bot.call();", "bot.call needs a key")]
    [InlineData("bot.call('chat.send', undefined);", "bot.call needs a value for argument 1")]
    public async Task A_required_argument_left_out_is_a_clear_script_error_and_nothing_reaches_the_host(
        string js,
        string expectedError
    )
    {
        (ScriptExecutionOutcomeResult outcome, RecordingBridge bridge, List<string> sent) =
            await Run(js);

        outcome.Outcome.Should().Be(ScriptExecutionOutcome.Faulted);
        outcome.ErrorMessage.Should().Contain(expectedError);
        bridge.Calls.Should().BeEmpty();
        sent.Should().BeEmpty();
        outcome.ChatOutput.Should().BeNull();
    }

    [Fact]
    public async Task Optional_arguments_left_out_keep_their_defaults_and_never_send_undefined()
    {
        (ScriptExecutionOutcomeResult outcome, RecordingBridge bridge, _) = await Run(
            """
            nnz.api.widget.emit('w', 'e');
            nnz.api.schedule.pipeline('p', 5);
            nnz.api.tts.setVoice('viewer');
            nnz.api.tts.speak('hi');
            """
        );

        outcome.Outcome.Should().Be(ScriptExecutionOutcome.Success);
        bridge
            .Calls.Select(c => $"{c.Key}({string.Join('|', c.Args)})")
            .Should()
            .Equal(
                "widget.emit(w|e)",
                "schedule.pipeline(p|5|{})",
                "tts.voice.set(viewer|)",
                "tts.speak(hi)"
            );
        bridge
            .Calls.SelectMany(c => c.Args)
            .Should()
            .NotContain(a => a.Contains("undefined") || a == "null");
    }
}
