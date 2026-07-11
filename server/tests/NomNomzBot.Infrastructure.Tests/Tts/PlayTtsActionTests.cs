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
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Infrastructure.Tts.PipelineActions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves the <c>play_tts</c> pipeline action (tts.md §6): it resolves the <c>text</c> template and hands the
/// utterance to the dispatch service, returning success on dispatch and surfacing the gate's reason on rejection.
/// A missing/empty text fails loudly instead of dispatching junk.
/// </summary>
public sealed class PlayTtsActionTests
{
    private static readonly Guid Channel = Guid.Parse("019f2a00-1111-7000-8000-000000000001");

    private static PipelineExecutionContext Context() =>
        new()
        {
            BroadcasterId = Channel,
            TriggeredByUserId = "viewer-9",
            TriggeredByDisplayName = "viewer",
            MessageId = "m1",
            RawMessage = "!tts hi",
            CancellationToken = default,
        };

    private static ActionDefinition Action(params (string Key, object Value)[] p) =>
        new()
        {
            Type = "play_tts",
            Parameters = p.ToDictionary(
                x => x.Key,
                x => JsonSerializer.SerializeToElement(x.Value)
            ),
        };

    private static (PlayTtsAction Action, ITtsDispatchService Dispatch) Build(string resolvedText)
    {
        ITemplateResolver resolver = Substitute.For<ITemplateResolver>();
        resolver
            .ResolveAsync(
                Arg.Any<string>(),
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<Guid?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(resolvedText));

        ITtsDispatchService dispatch = Substitute.For<ITtsDispatchService>();
        return (new PlayTtsAction(resolver, dispatch), dispatch);
    }

    [Fact]
    public async Task ExecuteAsync_ResolvesText_AndDispatchesIt()
    {
        (PlayTtsAction action, ITtsDispatchService dispatch) = Build("hello resolved");
        dispatch
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new TtsDispatchOutcome(
                        TtsDispatchDisposition.Dispatched,
                        "v1",
                        "edge",
                        14,
                        900,
                        "https://bot.local/sounds/tts.mp3"
                    )
                )
            );

        ActionResult result = await action.ExecuteAsync(Context(), Action(("text", "{{args}}")));

        result.Succeeded.Should().BeTrue();
        await dispatch
            .Received(1)
            .RequestSpeakAsync(
                Arg.Is<TtsSpeakRequest>(r =>
                    r.Text == "hello resolved"
                    && r.BroadcasterId == Channel
                    && r.RequestedByTwitchUserId == "viewer-9"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ExecuteAsync_MissingText_FailsWithoutDispatch()
    {
        (PlayTtsAction action, ITtsDispatchService dispatch) = Build("unused");

        ActionResult result = await action.ExecuteAsync(Context(), Action());

        result.Succeeded.Should().BeFalse();
        await dispatch
            .DidNotReceive()
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_DispatchRejects_SurfacesTheReason()
    {
        (PlayTtsAction action, ITtsDispatchService dispatch) = Build("hello");
        dispatch
            .RequestSpeakAsync(Arg.Any<TtsSpeakRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Result.Failure<TtsDispatchOutcome>(
                    "TTS is disabled for this channel.",
                    "FEATURE_DISABLED"
                )
            );

        ActionResult result = await action.ExecuteAsync(Context(), Action(("text", "{{args}}")));

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Be("TTS is disabled for this channel.");
    }
}
