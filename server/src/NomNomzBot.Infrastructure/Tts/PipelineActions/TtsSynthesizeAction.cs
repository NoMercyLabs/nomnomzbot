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
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;

namespace NomNomzBot.Infrastructure.Tts.PipelineActions;

/// <summary>
/// Pipeline action <c>tts_synthesize</c>: synthesizes an utterance server-side and hands the RESULT to the
/// rest of the pipeline instead of playing it — the general-purpose sibling of <c>play_tts</c> (which
/// speaks immediately on the standard TTS overlay bus). Any step after this one can read
/// <c>{{tts.audioUrl}}</c> (a playable URL) and <c>{{tts.durationMs}}</c> (the REAL measured audio
/// length, not a guess) — e.g. to hand an already-synthesized clip + its exact duration to a
/// <c>widget_event</c> payload, or to size a <c>wait</c> step precisely around it. Does not touch the
/// TTS gate/censor/ledger/mod-approval pipeline (that is <c>play_tts</c>'s job for viewer-facing
/// messages) — this is for a streamer-authored line inside their own pipeline.
/// </summary>
public sealed class TtsSynthesizeAction : ICommandAction
{
    private readonly ITemplateResolver _resolver;
    private readonly ITtsService _tts;
    private readonly ITtsConfigService _ttsConfig;
    private readonly ISoundClipStore _audioStore;

    public string ActionType => "tts_synthesize";

    public LocalizedText Category => new("pipeline.category.tts");

    public LocalizedText Description => new("pipeline.tts_synthesize.description");

    public bool ResolvesOwnTemplates => true;

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "text",
                PipelineActionFieldKind.Text,
                Required: true,
                Templated: true,
                Description: new("pipeline.tts_synthesize.text.help")
            ),
            new(
                "voice",
                PipelineActionFieldKind.Voice,
                Templated: true,
                Description: new("pipeline.tts_synthesize.voice.help")
            ),
        ];

    public TtsSynthesizeAction(
        ITemplateResolver resolver,
        ITtsService tts,
        ITtsConfigService ttsConfig,
        ISoundClipStore audioStore
    )
    {
        _resolver = resolver;
        _tts = tts;
        _ttsConfig = ttsConfig;
        _audioStore = audioStore;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string template = action.GetString("text") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(template))
            return ActionResult.Failure("tts_synthesize requires a 'text' parameter.");

        string text = await _resolver.ResolveAsync(
            template,
            ctx.Variables,
            ctx.BroadcasterId,
            ctx.CancellationToken
        );
        if (string.IsNullOrWhiteSpace(text))
            return ActionResult.Failure("tts_synthesize resolved to empty text.");

        string voiceTemplate = action.GetString("voice") ?? string.Empty;
        string? voiceId = null;
        if (!string.IsNullOrWhiteSpace(voiceTemplate))
        {
            string resolvedVoice = await _resolver.ResolveAsync(
                voiceTemplate,
                ctx.Variables,
                ctx.BroadcasterId,
                ctx.CancellationToken
            );
            voiceId = string.IsNullOrWhiteSpace(resolvedVoice) ? null : resolvedVoice;
        }
        if (string.IsNullOrWhiteSpace(voiceId))
        {
            Result<TtsConfigDto> config = await _ttsConfig.GetConfigAsync(
                ctx.BroadcasterId,
                ctx.CancellationToken
            );
            voiceId = config.IsSuccess ? config.Value.DefaultVoiceId : null;
        }
        if (string.IsNullOrWhiteSpace(voiceId))
            return ActionResult.Failure(
                "tts_synthesize: no TTS voice configured for this channel."
            );

        // A templated `voice` field can resolve to anything at runtime — validate it against the catalogue
        // (case-insensitively, tts.md §6.2) before synthesizing, so an unknown voice fails honestly instead of
        // the provider silently substituting a different one and this step reporting the requested id as spoken.
        if (!string.IsNullOrWhiteSpace(voiceTemplate))
        {
            Result<PagedList<TtsVoiceDto>> voiceSearch = await _ttsConfig.SearchVoicesAsync(
                new TtsVoiceQuery(Q: voiceId, PageSize: 200),
                ctx.CancellationToken
            );
            bool voiceKnown =
                voiceSearch.IsSuccess
                && voiceSearch.Value.Items.Any(v =>
                    string.Equals(v.Id, voiceId, StringComparison.OrdinalIgnoreCase)
                );
            if (!voiceKnown)
                return ActionResult.Failure(
                    $"tts_synthesize: TTS voice '{voiceId}' does not exist."
                );
        }

        TtsResult synth = await _tts.SynthesizeAsync(text, voiceId, ctx.CancellationToken);
        if (synth.AudioData.Length == 0)
            return ActionResult.Failure("tts_synthesize: the TTS provider returned no audio.");

        string fileName = $"tts-{Guid.CreateVersion7():n}.mp3";
        using MemoryStream audio = new(synth.AudioData);
        Result<string> stored = await _audioStore.PutAsync(
            ctx.BroadcasterId,
            fileName,
            audio,
            "audio/mpeg",
            ctx.CancellationToken
        );
        if (stored.IsFailure)
            return ActionResult.Failure(
                $"tts_synthesize: could not store the synthesized audio ({stored.ErrorMessage})."
            );

        Result<string> url = await _audioStore.GetPlaybackUrlAsync(
            stored.Value,
            ctx.CancellationToken
        );
        if (url.IsFailure)
            return ActionResult.Failure(
                $"tts_synthesize: could not resolve a playback URL ({url.ErrorMessage})."
            );

        ctx.Variables["tts.audioUrl"] = url.Value;
        ctx.Variables["tts.durationMs"] = synth.DurationMs.ToString();
        ctx.Variables["tts.voiceId"] = synth.VoiceId;

        return ActionResult.Success(
            $"tts_synthesize:{synth.VoiceId} durationMs={synth.DurationMs}"
        );
    }
}
