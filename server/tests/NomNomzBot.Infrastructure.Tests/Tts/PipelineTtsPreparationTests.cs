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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline.CoreActions;
using NomNomzBot.Infrastructure.Tests.Common;
using NomNomzBot.Infrastructure.Tts.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

public sealed class PipelineTtsPreparationTests
{
    private static readonly Guid Channel = Guid.Parse("019f2a00-1111-7000-8000-000000000099");

    private sealed class Harness
    {
        public required PipelineEngine Engine { get; init; }
        public required ITtsDispatchService Dispatch { get; init; }
        public required TimerCountingTimeProvider Clock { get; init; }
        public List<string> Synthesized { get; } = [];
        public List<string> Played { get; } = [];
        public Dictionary<string, TaskCompletionSource<Result<TtsPreparedClip?>>> Pending { get; } =
        [];
    }

    private static Harness Build()
    {
        TimerCountingTimeProvider clock = new(DateTimeOffset.Parse("2026-11-03T00:00:00Z"));
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string text = call.ArgAt<string>(0);
                IDictionary<string, string> variables = call.ArgAt<IDictionary<string, string>>(1);
                foreach ((string key, string value) in variables)
                    text = text.Replace("{" + key + "}", value, StringComparison.OrdinalIgnoreCase);
                return Task.FromResult(text);
            });
        ITtsDispatchService dispatch = Substitute.For<ITtsDispatchService>();
        IChannelRegistry registry = Substitute.For<IChannelRegistry>();
        registry.Get(Arg.Any<Guid>()).Returns((ChannelContext?)null);
        IApplicationDbContext db = Substitute.For<IApplicationDbContext>();
        Harness? harness = null;
        dispatch
            .PrepareSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                string text = call.ArgAt<TtsSpeakRequest>(0).Text;
                harness!.Synthesized.Add(text);
                TaskCompletionSource<Result<TtsPreparedClip?>> pending = new(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                harness.Pending[text] = pending;
                return Task.FromResult(new TtsPreparation(pending.Task));
            });
        dispatch
            .RequestPreparedSpeakAsync(
                Arg.Any<TtsSpeakRequest>(),
                Arg.Any<TtsPreparedClip?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                string text = call.ArgAt<TtsSpeakRequest>(0).Text;
                harness!.Played.Add(text);
                return Result.Success(
                    new TtsDispatchOutcome(
                        TtsDispatchDisposition.Dispatched,
                        "voice",
                        "edge",
                        text.Length,
                        1000,
                        "data:audio/mpeg;base64,AQ=="
                    )
                );
            });
        dispatch
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                string text = call.ArgAt<TtsSpeakRequest>(0).Text;
                harness!.Synthesized.Add(text);
                harness.Played.Add(text);
                return Result.Success(
                    new TtsDispatchOutcome(
                        TtsDispatchDisposition.Dispatched,
                        "voice",
                        "edge",
                        text.Length,
                        1000,
                        "data:audio/mpeg;base64,AQ=="
                    )
                );
            });
        PipelineEngine engine = new(
            db,
            registry,
            [new PlayTtsAction(resolver, dispatch, clock), new SetVariableAction()],
            [new UserRoleCondition()],
            resolver,
            NullLogger<PipelineEngine>.Instance,
            clock
        );
        harness = new()
        {
            Engine = engine,
            Dispatch = dispatch,
            Clock = clock,
        };
        return harness;
    }

    private static PipelineRequest Request(string json) =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "viewer",
            TriggeredByDisplayName = "Viewer",
            MessageId = "message",
            RawMessage = "",
            PipelineJson = json,
        };

    private static void Finish(Harness harness, string text)
    {
        TtsResult audio = new([1], 1000, "voice", "edge");
        harness
            .Pending[text]
            .SetResult(
                Result.Success<TtsPreparedClip?>(
                    new TtsPreparedClip(
                        Channel,
                        text,
                        "voice",
                        "self_host",
                        "edge",
                        null,
                        null,
                        null,
                        audio,
                        10
                    )
                )
            );
    }

    private static async Task WaitForPlayedAsync(Harness harness, int count)
    {
        for (int i = 0; i < 1000 && harness.Played.Count < count; i++)
            await Task.Yield();
        harness.Played.Should().HaveCount(count);
    }

    [Fact]
    public async Task Two_clips_synthesize_concurrently_but_play_in_step_order()
    {
        Harness harness = Build();
        Task<PipelineExecutionResult> run = harness.Engine.ExecuteAsync(
            Request(
                """{"steps":[{"action":{"type":"play_tts","text":"first"}},{"action":{"type":"play_tts","text":"second"}}]}"""
            )
        );

        harness.Synthesized.Should().Equal("first", "second");
        Finish(harness, "second");
        harness.Played.Should().BeEmpty();
        Finish(harness, "first");
        PipelineExecutionResult result = await run;

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        harness.Played.Should().Equal("first", "second");
    }

    [Fact]
    public async Task Sync_clip_holds_the_next_play_until_its_duration_has_passed()
    {
        Harness harness = Build();
        Task<PipelineExecutionResult> run = harness.Engine.ExecuteAsync(
            Request(
                """{"steps":[{"action":{"type":"play_tts","text":"first","mode":"sync"}},{"action":{"type":"play_tts","text":"second"}}]}"""
            )
        );
        Finish(harness, "first");
        Finish(harness, "second");
        await WaitForPlayedAsync(harness, 1);
        await harness.Clock.WaitForTimersAsync(1);

        harness.Played.Should().Equal("first");
        harness.Clock.Advance(TimeSpan.FromMilliseconds(1249));
        harness.Played.Should().Equal("first");
        harness.Clock.Advance(TimeSpan.FromMilliseconds(1));
        PipelineExecutionResult result = await run;
        result.Outcome.Should().Be(PipelineOutcome.Completed);
        harness.Played.Should().Equal("first", "second");
    }

    [Fact]
    public async Task Skipped_conditional_discards_its_prepared_audio()
    {
        Harness harness = Build();
        Task<PipelineExecutionResult> run = harness.Engine.ExecuteAsync(
            Request(
                """{"steps":[{"condition":{"type":"user_role","min_role":"moderator"},"action":{"type":"play_tts","text":"skipped"}}]}"""
            )
        );
        harness.Synthesized.Should().Equal("skipped");
        Finish(harness, "skipped");
        PipelineExecutionResult result = await run;

        result.StepsSkipped.Should().Be(1);
        harness.Played.Should().BeEmpty();
    }

    [Fact]
    public async Task Variable_written_by_earlier_step_is_synthesized_at_its_turn()
    {
        Harness harness = Build();
        PipelineExecutionResult result = await harness.Engine.ExecuteAsync(
            Request(
                """{"steps":[{"action":{"type":"set_variable","name":"line","value":"from earlier step"}},{"action":{"type":"play_tts","text":"{line}"}}]}"""
            )
        );

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        harness.Synthesized.Should().Equal("from earlier step");
        harness.Played.Should().Equal("from earlier step");
    }

    [Fact]
    public async Task Trigger_variable_known_at_start_is_prepared_before_its_turn()
    {
        Harness harness = Build();
        PipelineRequest request = Request(
            """{"steps":[{"action":{"type":"set_variable","name":"unrelated","value":"done"}},{"action":{"type":"play_tts","text":"hello {user.name}"}}]}"""
        );
        request.InitialVariables["user.name"] = "Stoney";

        Task<PipelineExecutionResult> run = harness.Engine.ExecuteAsync(request);
        harness.Synthesized.Should().Equal("hello Stoney");
        Finish(harness, "hello Stoney");
        PipelineExecutionResult result = await run;

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        harness.Played.Should().Equal("hello Stoney");
    }

    [Fact]
    public async Task Preparation_error_uses_the_live_dispatch_path_at_the_step_turn()
    {
        Harness harness = Build();
        harness
            .Dispatch.PrepareSpeakAsync(
                Arg.Is<TtsSpeakRequest>(request => request.Text == "hello"),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
                Task.FromException<TtsPreparation>(
                    new InvalidOperationException("preparation unavailable")
                )
            );

        PipelineExecutionResult result = await harness.Engine.ExecuteAsync(
            Request("""{"steps":[{"action":{"type":"play_tts","text":"hello"}}]}""")
        );

        result.Outcome.Should().Be(PipelineOutcome.Completed);
        harness.Played.Should().Equal("hello");
        harness.Synthesized.Should().Equal("hello");
    }
}
