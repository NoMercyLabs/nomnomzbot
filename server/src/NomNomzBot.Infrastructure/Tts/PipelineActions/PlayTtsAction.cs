// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;
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

    public LocalizedText Category => new("pipeline.category.tts");

    public LocalizedText Description => new("pipeline.play_tts.description");
    public bool ResolvesOwnTemplates => true;

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "text",
                PipelineActionFieldKind.Text,
                Required: true,
                Templated: true,
                Description: new("pipeline.play_tts.text.help")
            ),
            new(
                "voice",
                PipelineActionFieldKind.Voice,
                Templated: true,
                Description: new("pipeline.play_tts.voice.help")
            ),
            new(
                "as",
                PipelineActionFieldKind.Text,
                Templated: true,
                Description: new("pipeline.play_tts.as.help")
            ),
        ];

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string template = action.GetString("text") ?? string.Empty;
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

        string voiceTemplate = action.GetString("voice") ?? string.Empty;
        string? voiceOverride = null;
        if (!string.IsNullOrWhiteSpace(voiceTemplate))
        {
            string resolvedVoice = await _resolver.ResolveAsync(
                voiceTemplate,
                ctx.Variables,
                ctx.BroadcasterId,
                ctx.CancellationToken
            );
            voiceOverride = string.IsNullOrWhiteSpace(resolvedVoice) ? null : resolvedVoice;
        }

        // WHOSE voice speaks this line. The bot's own lines (an event announcement, a snarky cheer intro)
        // must read in the CHANNEL's voice, while the viewer's own words read in theirs — a cheer with a
        // message is one flow with both, back to back. Empty/"user" keeps the trigger's voice; "bot" (or
        // "channel") resolves to the channel default by naming no viewer; anything else is a literal
        // platform user id, so a flow can read a line as a specific person.
        string speaker = ResolveSpeaker(
            await ResolveOptionalAsync(ctx, action, "as"),
            ctx.TriggeredByUserId
        );

        TtsSpeakRequest request = new(
            BroadcasterId: ctx.BroadcasterId,
            RequestedByUserId: Guid.Empty,
            RequestedByTwitchUserId: speaker,
            RequestedByDisplayName: ctx.TriggeredByDisplayName ?? string.Empty,
            Text: text,
            VoiceIdOverride: string.IsNullOrWhiteSpace(voiceOverride) ? null : voiceOverride,
            // The trigger's REAL bits and standing, not placeholders: hardcoding 0/"everyone" meant a
            // channel with a bits gate could never be spoken to through a pipeline, and the channel's
            // MinPermission floor was evaluated against a caller who always looked like a stranger.
            BitsAmount: ctx.Variables.TryGetValue("user.bits", out string? bits)
            && int.TryParse(bits, out int bitsAmount)
                ? bitsAmount
                : 0,
            CommunityStanding: ctx.Variables.TryGetValue("user.role", out string? role)
            && !string.IsNullOrWhiteSpace(role)
                ? role
                : "everyone",
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

    /// <summary>Resolves an optional templated field, returning empty when it is absent or resolves to nothing.</summary>
    private async Task<string> ResolveOptionalAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action,
        string field
    )
    {
        string template = action.GetString(field) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(template))
            return string.Empty;

        string resolved = await _resolver.ResolveAsync(
            template,
            ctx.Variables,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );
        return resolved.Trim();
    }

    /// <summary>
    /// Maps the <c>as</c> field onto the platform user id whose voice should read the line. The dispatch
    /// resolver falls back to the channel default when it is handed no viewer, so naming the bot is simply
    /// naming nobody — one rule, no second lookup path that could disagree with it.
    /// </summary>
    private static string ResolveSpeaker(string speakerField, string? triggeredByUserId) =>
        speakerField.ToLowerInvariant() switch
        {
            "" or "user" or "viewer" or "trigger" => triggeredByUserId ?? string.Empty,
            "bot" or "channel" or "broadcaster" or "default" => string.Empty,
            _ => speakerField,
        };
}
