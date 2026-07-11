// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;

namespace NomNomzBot.Infrastructure.Tts.PipelineActions;

/// <summary>
/// Pipeline action <c>play_tts</c> (tts.md §6). Resolves the <c>text</c> template and hands the utterance to
/// <see cref="ITtsDispatchService"/>, which gates it (enabled + character cap), synthesizes it, and plays it on
/// the overlay. Fails (with the gate's reason) when TTS is off, the text is empty/too long, or synthesis fails —
/// so the pipeline log tells the truth instead of silently swallowing a dropped utterance.
/// </summary>
public sealed class PlayTtsAction : ICommandAction
{
    private readonly ITemplateResolver _resolver;
    private readonly ITtsDispatchService _dispatch;

    public PlayTtsAction(ITemplateResolver resolver, ITtsDispatchService dispatch)
    {
        _resolver = resolver;
        _dispatch = dispatch;
    }

    public string ActionType => "play_tts";

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string template = action.GetString("text") ?? action.GetString("message") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(template))
            return ActionResult.Failure("play_tts requires a 'text' parameter.");

        string text = await _resolver.ResolveAsync(
            template,
            ctx.Variables,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );
        if (string.IsNullOrWhiteSpace(text))
            return ActionResult.Failure("play_tts resolved to empty text.");

        string? voiceOverride = action.GetString("voice");

        TtsSpeakRequest request = new(
            BroadcasterId: ctx.BroadcasterId,
            RequestedByUserId: Guid.Empty,
            RequestedByTwitchUserId: ctx.TriggeredByUserId ?? string.Empty,
            RequestedByDisplayName: ctx.TriggeredByDisplayName ?? string.Empty,
            Text: text,
            VoiceIdOverride: string.IsNullOrWhiteSpace(voiceOverride) ? null : voiceOverride,
            BitsAmount: 0,
            CommunityStanding: "everyone",
            SourceMessageId: ctx.MessageId,
            StreamId: null
        );

        Result<TtsDispatchOutcome> result = await _dispatch.RequestSpeakAsync(
            request,
            ctx.CancellationToken
        );
        if (result.IsFailure)
            return ActionResult.Failure(result.ErrorMessage ?? "TTS dispatch failed.");

        return ActionResult.Success(
            $"play_tts:{result.Value.VoiceId} chars={result.Value.CharacterCount}"
        );
    }
}
