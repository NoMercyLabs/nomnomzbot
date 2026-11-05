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

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;

namespace NomNomzBot.Infrastructure.Tts.PipelineActions;

/// <summary>
/// Assigns a viewer's TTS voice (the <c>!voice set</c> capability). With no <c>target_variable</c> resolved
/// it is self-service on the triggering user, the same as <c>!voice set</c>; with one resolved it assigns a
/// NAMED viewer's voice, the same as a moderator running <c>!voice set @viewer &lt;name&gt;</c>.
///
/// Parameters:
///   voice_id — the catalogue voice id to assign (required).
///   target_variable — the variable holding the target's platform user id; the triggering user when absent.
///
/// Usage example:
///   { "type": "tts_set_voice", "voice_id": "en-US-AnaNeural" }
/// </summary>
public sealed class TtsSetVoiceAction : ICommandAction
{
    private readonly ITtsConfigService _tts;

    public string ActionType => "tts_set_voice";

    public LocalizedText Category => new("pipeline.category.tts");

    public LocalizedText Description => new("pipeline.tts_set_voice.description");

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "voice_id",
                PipelineActionFieldKind.Voice,
                Required: true,
                Templated: true,
                Description: new("pipeline.tts_set_voice.voice_id.help")
            ),
            new(
                "target_variable",
                PipelineActionFieldKind.Text,
                Description: new("pipeline.tts_set_voice.target_variable.help")
            ),
        ];

    public TtsSetVoiceAction(ITtsConfigService tts) => _tts = tts;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string voiceId = action.GetString("voice_id") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(voiceId))
            return ActionResult.Failure("tts_set_voice: voice_id not resolved");

        SetUserVoiceDto request = new() { VoiceId = voiceId };
        string? targetUserId = TtsVoiceTargetSupport.ResolveTargetUserId(ctx, action);

        Result<UserTtsVoiceDto> result = targetUserId is null
            ? await _tts.SetOwnVoiceAsync(
                ctx.BroadcasterId,
                ctx.TriggeredByUserId,
                request,
                ctx.CancellationToken
            )
            : await _tts.SetUserVoiceAsync(
                ctx.BroadcasterId,
                targetUserId,
                request,
                ctx.CancellationToken
            );

        return result.IsSuccess
            ? ActionResult.Success($"voice set to {voiceId}")
            : ActionResult.Failure(result.ErrorMessage ?? "tts_set_voice failed");
    }
}

/// <summary>
/// Clears a viewer's assigned TTS voice back to the channel default (the <c>!voice clear</c> capability).
/// With no <c>target_variable</c> resolved it is self-service on the triggering user; with one resolved it
/// clears a NAMED viewer's assignment.
///
/// Parameters:
///   target_variable — the variable holding the target's platform user id; the triggering user when absent.
///
/// Usage example:
///   { "type": "tts_clear_voice" }
/// </summary>
public sealed class TtsClearVoiceAction : ICommandAction
{
    private readonly ITtsConfigService _tts;

    public string ActionType => "tts_clear_voice";

    public LocalizedText Category => new("pipeline.category.tts");

    public LocalizedText Description => new("pipeline.tts_clear_voice.description");

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "target_variable",
                PipelineActionFieldKind.Text,
                Description: new("pipeline.tts_clear_voice.target_variable.help")
            ),
        ];

    public TtsClearVoiceAction(ITtsConfigService tts) => _tts = tts;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? targetUserId = TtsVoiceTargetSupport.ResolveTargetUserId(ctx, action);

        Result cleared = targetUserId is null
            ? await _tts.ClearOwnVoiceAsync(
                ctx.BroadcasterId,
                ctx.TriggeredByUserId,
                ctx.CancellationToken
            )
            : await _tts.ClearUserVoiceAsync(
                ctx.BroadcasterId,
                targetUserId,
                ctx.CancellationToken
            );

        return cleared.IsSuccess
            ? ActionResult.Success("voice cleared")
            : ActionResult.Failure(cleared.ErrorMessage ?? "tts_clear_voice failed");
    }
}

/// <summary>Shared target resolution for <see cref="TtsSetVoiceAction"/> / <see cref="TtsClearVoiceAction"/>.</summary>
internal static class TtsVoiceTargetSupport
{
    /// <summary>The target's platform user id named by <c>target_variable</c>, or null for self-service.</summary>
    public static string? ResolveTargetUserId(PipelineExecutionContext ctx, ActionDefinition action)
    {
        string targetVariable = action.GetString("target_variable") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetVariable))
            return null;

        string? targetUserId = ctx.Variables.GetValueOrDefault(targetVariable);
        return string.IsNullOrWhiteSpace(targetUserId) ? null : targetUserId;
    }
}
