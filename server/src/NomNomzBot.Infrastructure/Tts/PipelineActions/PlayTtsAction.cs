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

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private const double MinRatePercent = -50;
    private const double MaxRatePercent = 50;

    private const string ModeAsync = "async";
    private const string ModeSync = "sync";

    /// <summary>The longest a <c>sync</c> step waits for one clip; a longer clip waits this long.</summary>
    private const int MaxSyncWaitMs = 60_000;

    /// <summary>Time for the overlay to fetch and start the clip, added to the clip's own length.</summary>
    private const int OverlayStartAllowanceMs = 250;

    private readonly ITemplateResolver _resolver;
    private readonly ITtsDispatchService _dispatch;
    private readonly TimeProvider _time;
    private readonly ConcurrentDictionary<
        (string RunId, string StepId),
        Task<Result<TtsPreparedClip?>>
    > _prepared = new();

    public PlayTtsAction(
        ITemplateResolver resolver,
        ITtsDispatchService dispatch,
        TimeProvider? time = null
    )
    {
        _resolver = resolver;
        _dispatch = dispatch;
        _time = time ?? TimeProvider.System;
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
                Templated: true,
                Description: new("pipeline.play_tts.text.help")
            ),
            new(
                "segments",
                PipelineActionFieldKind.Text,
                Templated: true,
                Description: new("pipeline.play_tts.segments.help")
            ),
            new(
                "voice",
                PipelineActionFieldKind.Voice,
                Templated: true,
                Description: new("pipeline.play_tts.voice.help")
            ),
            new(
                "as",
                PipelineActionFieldKind.TtsSpeaker,
                Templated: true,
                Description: new("pipeline.play_tts.as.help")
            ),
            new(
                "rate",
                PipelineActionFieldKind.Text,
                Templated: true,
                Description: new("pipeline.play_tts.rate.help")
            ),
            new(
                "mode",
                PipelineActionFieldKind.Enum,
                Options: [ModeAsync, ModeSync],
                Description: new("pipeline.play_tts.mode.help")
            ),
            new(
                "scene_token",
                PipelineActionFieldKind.Text,
                Templated: true,
                Description: new("pipeline.play_tts.scene_token.help")
            ),
        ];

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    ) => await ExecuteCoreAsync(ctx, action, null);

    public async Task<ActionResult> ExecutePreparedStepAsync(
        PipelineExecutionContext ctx,
        string stepId,
        ActionDefinition action
    )
    {
        if (
            !_prepared.TryRemove(
                (ctx.ExecutionId, stepId),
                out Task<Result<TtsPreparedClip?>>? task
            )
        )
            return await ExecuteAsync(ctx, action);
        Result<TtsPreparedClip?> prepared = await task;
        return await ExecuteCoreAsync(ctx, action, prepared);
    }

    public async Task StartPreparationAsync(
        PipelineExecutionContext ctx,
        string stepId,
        ActionDefinition action,
        CancellationToken ct
    )
    {
        try
        {
            PipelineExecutionContext snapshot = new()
            {
                BroadcasterId = ctx.BroadcasterId,
                TriggeredByUserId = ctx.TriggeredByUserId,
                TriggeredByPlatform = ctx.TriggeredByPlatform,
                TriggeredByDisplayName = ctx.TriggeredByDisplayName,
                MessageId = ctx.MessageId,
                RedemptionId = ctx.RedemptionId,
                RewardId = ctx.RewardId,
                ChannelEventId = ctx.ChannelEventId,
                RawMessage = ctx.RawMessage,
                CancellationToken = ct,
            };
            foreach ((string name, string value) in ctx.Variables)
                snapshot.Variables[name] = value;
            (TtsSpeakRequest? request, ActionResult? error) = await BuildRequestAsync(
                snapshot,
                action
            );
            if (request is null || error is not null)
                return;
            TtsPreparation preparation = await _dispatch.PrepareSpeakAsync(request, ct);
            _prepared[(ctx.ExecutionId, stepId)] = preparation.Clip;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Preparation is speculative. The step executes through the normal dispatch path at its turn.
        }
    }

    public void DiscardRun(string runId)
    {
        foreach ((string run, string step) in _prepared.Keys)
            if (run == runId)
                _prepared.TryRemove((run, step), out _);
    }

    public static bool CanPrepare(ActionDefinition action, IReadOnlySet<string> knownVariables)
    {
        if (action.Parameters is null)
            return false;
        foreach (string field in new[] { "text", "voice", "as", "rate", "segments" })
        {
            if (!action.Parameters.TryGetValue(field, out JsonElement value))
                continue;
            if (!UsesOnlyKnownVariables(value, knownVariables))
                return false;
        }
        return true;
    }

    private static bool UsesOnlyKnownVariables(JsonElement value, IReadOnlySet<string> known)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            string content = value.GetString() ?? string.Empty;
            if (content.StartsWith('['))
            {
                try
                {
                    using JsonDocument document = JsonDocument.Parse(content);
                    return UsesOnlyKnownVariables(document.RootElement, known);
                }
                catch (JsonException) { }
            }
            foreach (Match match in Regex.Matches(content, @"(?<!\\)\{([^{}]+)\}"))
                if (!known.Contains(match.Groups[1].Value.Trim()))
                    return false;
            return true;
        }
        if (value.ValueKind == JsonValueKind.Object)
            return value
                .EnumerateObject()
                .All(property => UsesOnlyKnownVariables(property.Value, known));
        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().All(item => UsesOnlyKnownVariables(item, known));
        return true;
    }

    private async Task<ActionResult> ExecuteCoreAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action,
        Result<TtsPreparedClip?>? prepared
    )
    {
        string mode = (action.GetString("mode") ?? string.Empty).Trim().ToLowerInvariant();
        if (mode.Length == 0)
            mode = ModeAsync;
        if (mode is not (ModeAsync or ModeSync))
            return ActionResult.Failure(
                $"play_tts 'mode' must be '{ModeAsync}' or '{ModeSync}', got '{mode}'."
            );

        (TtsSpeakRequest? request, ActionResult? error) = await BuildRequestAsync(ctx, action);
        if (error is not null)
            return error;
        if (request is null)
            return ActionResult.Failure("play_tts could not resolve its request.");

        Result<TtsDispatchOutcome> result = prepared is { IsSuccess: true }
            ? await _dispatch.RequestPreparedSpeakAsync(
                request,
                prepared.Value,
                ctx.CancellationToken
            )
            : await _dispatch.RequestSpeakAsync(request, ctx.CancellationToken);
        if (result.IsFailure)
            return ActionResult.Failure(result.ErrorMessage ?? "TTS dispatch failed.");

        TtsDispatchOutcome outcome = result.Value;
        ctx.Variables["tts.durationMs"] = outcome.DurationMs.ToString(
            System.Globalization.CultureInfo.InvariantCulture
        );
        ctx.Variables["tts.voiceId"] = outcome.VoiceId;

        if (mode == ModeSync && outcome.Disposition == TtsDispatchDisposition.Dispatched)
            await Task.Delay(
                TimeSpan.FromMilliseconds(
                    Math.Clamp(outcome.DurationMs, 0, MaxSyncWaitMs) + OverlayStartAllowanceMs
                ),
                _time,
                ctx.CancellationToken
            );

        return ActionResult.Success($"play_tts:{outcome.VoiceId} chars={outcome.CharacterCount}");
    }

    private async Task<(TtsSpeakRequest? Request, ActionResult? Error)> BuildRequestAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        List<TtsSpeakSegment>? segments = null;
        string text;
        bool speaksViewerWords;
        string? markedText = null;
        if (action.Parameters?.TryGetValue("segments", out JsonElement rawSegments) == true)
        {
            (List<TtsSpeakSegment>? parsed, string? segmentError) = await ResolveSegmentsAsync(
                rawSegments,
                ctx
            );
            if (parsed is null)
                return (
                    null,
                    ActionResult.Failure(segmentError ?? "play_tts 'segments' is invalid.")
                );
            segments = parsed;
            speaksViewerWords = ViewerWordsTemplate.IsIn(rawSegments.GetRawText());
            text = string.Join(' ', parsed.Select(p => p.Text));
        }
        else
        {
            string template = action.GetString("text") ?? string.Empty;
            speaksViewerWords = ViewerWordsTemplate.IsIn(template);
            if (string.IsNullOrWhiteSpace(template))
                return (null, ActionResult.Failure("play_tts requires a 'text' parameter."));

            text = await _resolver.ResolveAsync(
                template,
                ctx.Variables,
                ctx.BroadcasterId,
                ctx.CancellationToken
            );
            if (string.IsNullOrWhiteSpace(text))
                return (null, ActionResult.Failure("play_tts resolved to empty text."));

            // The same template again with the viewer's words marked, to tell them from the bot's own words.
            if (TtsViewerWords.Words(ctx.Variables).Count > 0)
                markedText = await _resolver.ResolveAsync(
                    template,
                    TtsViewerWords.Mark(ctx.Variables),
                    ctx.BroadcasterId,
                    ctx.CancellationToken
                );
        }

        if (
            ctx.Variables.TryGetValue("input.held", out string? held)
            && held.Equals("true", StringComparison.OrdinalIgnoreCase)
            && ctx.Variables.TryGetValue("input", out string? input)
            && !string.IsNullOrWhiteSpace(input)
            && (
                speaksViewerWords
                || text.Contains(input, StringComparison.Ordinal)
                || segments?.Any(segment => segment.Text.Contains(input, StringComparison.Ordinal))
                    == true
            )
        )
            return (null, ActionResult.Failure("Redemption text is held by AutoMod."));

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

        // WHOSE voice speaks this line. Blank (or "bot"/"channel"/"default") is the bot's own line and reads
        // in the CHANNEL's voice. "user"/"viewer"/"trigger" is the triggering viewer's own words (a TTS
        // reward) and reads in their saved voice; "broadcaster" reads in the channel owner's saved voice,
        // like the old bot; anything else is a literal platform user id, so a flow can read a line as a
        // specific person.
        string asTemplate = action.GetString("as") ?? string.Empty;
        string asField = string.IsNullOrWhiteSpace(asTemplate)
            ? string.Empty
            : (
                await _resolver.ResolveAsync(
                    asTemplate,
                    ctx.Variables,
                    ctx.BroadcasterId,
                    ctx.CancellationToken
                )
            ).Trim();
        // A blank `as` is the bot's own line, unless the text carries the viewer's typed words. The announcement
        // around them is the bot's, read in the channel's voice; the words are the viewer's, read in their own
        // voice, in one clip. A line that is only the viewer's words (a TTS reward's {input}) is the viewer's
        // alone. An explicit `as` always wins and speaks the whole line.
        if (asField.Length == 0 && segments is null && markedText is not null)
        {
            List<TtsSpeakSegment>? mixed =
                TtsViewerWords.Split(markedText, ctx.TriggeredByUserId)
                ?? TtsViewerWords.SplitRendered(text, ctx.Variables, ctx.TriggeredByUserId);
            if (mixed is [{ Speaker: TtsSpeaker.Viewer }])
                asField = "viewer";
            else if (mixed is not null)
                segments = mixed;
        }

        if (asField.Length == 0 && speaksViewerWords && segments is null)
            asField = "viewer";
        string speaker = await ResolveSpeakerAsync(asField, ctx);

        string rateTemplate = action.GetString("rate") ?? string.Empty;
        double? ratePercent = null;
        if (!string.IsNullOrWhiteSpace(rateTemplate))
        {
            string resolvedRate = await _resolver.ResolveAsync(
                rateTemplate,
                ctx.Variables,
                ctx.BroadcasterId,
                ctx.CancellationToken
            );
            if (!TryParseRate(resolvedRate, out ratePercent))
                return (
                    null,
                    ActionResult.Failure(
                        $"play_tts 'rate' must be a percent like +30% or -20%, got '{resolvedRate}'."
                    )
                );
        }

        string sceneTokenTemplate = action.GetString("scene_token") ?? string.Empty;
        string? sceneToken = null;
        if (!string.IsNullOrWhiteSpace(sceneTokenTemplate))
        {
            string resolvedSceneToken = await _resolver.ResolveAsync(
                sceneTokenTemplate,
                ctx.Variables,
                ctx.BroadcasterId,
                ctx.CancellationToken
            );
            sceneToken = string.IsNullOrWhiteSpace(resolvedSceneToken) ? null : resolvedSceneToken;
        }

        TtsSpeakRequest request = new(
            BroadcasterId: ctx.BroadcasterId,
            RequestedByUserId: Guid.Empty,
            RequestedByTwitchUserId: speaker,
            RequestedByDisplayName: ctx.TriggeredByDisplayName,
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
            StreamId: null,
            ChannelEventId: ctx.ChannelEventId,
            RatePercent: ratePercent,
            AssignVoiceIfMissing: !asField.Equals(
                "broadcaster",
                StringComparison.OrdinalIgnoreCase
            ),
            Segments: segments,
            Speaker: IsBotSpeaker(asField) ? TtsSpeaker.Bot : TtsSpeaker.Viewer,
            SpokenNames: SpokenNameVariables.From(ctx.Variables, ctx.TriggeredByDisplayName),
            Emotes: TtsViewerWords.EmotesIn(ctx.Variables, ctx.TriggeredByDisplayName),
            SceneToken: sceneToken
        );

        return (request, null);
    }

    /// <summary>
    /// Reads the <c>segments</c> field — a JSON array (or JSON text of one) of
    /// blocks. A text block is <c>{text, voice, rate, pitch, volume, stress, breakAfterMs}</c>; a pause block is
    /// <c>{pause}</c>. Blocks combine in any order. Every value goes through the template resolver, or the whole
    /// list comes from one variable. Returns the error message instead of segments when the field is not a non-empty array
    /// of objects that each carry <c>text</c>, or a rate/pitch is not a number.
    /// </summary>
    private async Task<(List<TtsSpeakSegment>? Segments, string? Error)> ResolveSegmentsAsync(
        JsonElement raw,
        PipelineExecutionContext ctx
    )
    {
        JsonDocument? owned = null;
        try
        {
            JsonElement array = raw;
            // A list that arrives whole in one variable (a program hands over its own blocks) is used as it
            // is: its words are data, so no template in them is resolved a second time.
            bool literal = false;
            if (raw.ValueKind == JsonValueKind.String)
            {
                string json = raw.GetString() ?? string.Empty;
                literal = !LooksLikeJsonArray(json);
                if (literal)
                    json = await ResolveAsync(json, ctx);
                try
                {
                    owned = JsonDocument.Parse(json);
                    array = owned.RootElement;
                }
                catch (JsonException)
                {
                    return (null, "play_tts 'segments' is not valid JSON.");
                }
            }

            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
                return (null, "play_tts 'segments' must be a non-empty array.");

            List<TtsSpeakSegment> segments = [];
            int index = 0;
            foreach (JsonElement item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    return (null, $"play_tts segment {index} must be an object.");

                // A pause block: silence after the block before it. A pause with nothing before it is dropped.
                if (!item.TryGetProperty("text", out JsonElement textElement))
                {
                    if (!item.TryGetProperty("pause", out _))
                        return (null, $"play_tts segment {index} needs a 'text' or a 'pause'.");
                    int pauseMs = await ResolveMillisecondsAsync(item, "pause", ctx, literal);
                    if (segments.Count > 0)
                        segments[^1] = segments[^1] with
                        {
                            BreakAfterMs = segments[^1].BreakAfterMs + pauseMs,
                        };
                    index++;
                    continue;
                }

                if (textElement.ValueKind != JsonValueKind.String)
                    return (null, $"play_tts segment {index} needs a 'text' string.");

                string voice = (await ResolveFieldAsync(item, "voice", ctx, literal)).Trim();
                if (
                    !TryParseRate(
                        await ResolveFieldAsync(item, "rate", ctx, literal),
                        out double? rate
                    )
                    || !TryParseRate(
                        await ResolveFieldAsync(item, "pitch", ctx, literal),
                        out double? pitch
                    )
                    || !TryParseRate(
                        await ResolveFieldAsync(item, "volume", ctx, literal),
                        out double? volume
                    )
                )
                    return (
                        null,
                        $"play_tts segment {index} 'rate', 'pitch' and 'volume' must be numbers."
                    );

                string stress = await ResolveFieldAsync(item, "stress", ctx, literal);
                segments.Add(
                    new(
                        await ResolveFieldAsync(item, "text", ctx, literal),
                        voice.Length == 0 ? null : voice,
                        rate,
                        pitch,
                        await ResolveMillisecondsAsync(item, "breakAfterMs", ctx, literal),
                        VolumePercent: volume,
                        Stressed: stress.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)
                    )
                );
                index++;
            }

            if (segments.Count == 0)
                return (null, "play_tts 'segments' needs at least one block with 'text'.");

            return (segments, null);
        }
        finally
        {
            owned?.Dispose();
        }
    }

    private static bool LooksLikeJsonArray(string text) => text.TrimStart().StartsWith('[');

    private async Task<int> ResolveMillisecondsAsync(
        JsonElement item,
        string name,
        PipelineExecutionContext ctx,
        bool literal
    )
    {
        _ = int.TryParse(
            await ResolveFieldAsync(item, name, ctx, literal),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out int milliseconds
        );
        return Math.Max(0, milliseconds);
    }

    private async Task<string> ResolveFieldAsync(
        JsonElement item,
        string name,
        PipelineExecutionContext ctx,
        bool literal
    )
    {
        if (!item.TryGetProperty(name, out JsonElement element))
            return string.Empty;
        if (element.ValueKind != JsonValueKind.String)
            return element.ValueKind is JsonValueKind.True ? "true" : element.ToString();

        string value = element.GetString() ?? string.Empty;
        return literal ? value : await ResolveAsync(value, ctx);
    }

    private Task<string> ResolveAsync(string template, PipelineExecutionContext ctx) =>
        _resolver.ResolveAsync(template, ctx.Variables, ctx.BroadcasterId, ctx.CancellationToken);

    /// <summary>
    /// Reads a rate in the legacy form (<c>+30%</c>, <c>-20</c>, <c>15%</c>): a percent of the normal speed.
    /// Blank means no override. Anything that is not a finite number fails. A value outside
    /// <see cref="MinRatePercent"/>..<see cref="MaxRatePercent"/> is clamped to that range, the same range the
    /// TTS providers enforce before SSML.
    /// </summary>
    private static bool TryParseRate(string raw, out double? percent)
    {
        percent = null;
        string trimmed = raw.Trim().TrimEnd('%').Trim();
        if (trimmed.Length == 0)
            return true;
        if (
            !double.TryParse(
                trimmed,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double parsed
            ) || !double.IsFinite(parsed)
        )
            return false;
        percent = Math.Clamp(parsed, MinRatePercent, MaxRatePercent);
        return true;
    }

    /// <summary>
    /// Maps the <c>as</c> field onto the platform user id whose voice should read the line. The dispatch
    /// resolver falls back to the channel default when it is handed no viewer, so naming the bot is simply
    /// naming nobody. <c>broadcaster</c> names the channel owner by platform id: the dispatch resolver then
    /// uses the owner's saved voice, and the channel default when they have none.
    /// </summary>
    private static bool IsBotSpeaker(string speakerField) =>
        speakerField.ToLowerInvariant() is "" or "bot" or "channel" or "default";

    private async Task<string> ResolveSpeakerAsync(
        string speakerField,
        PipelineExecutionContext ctx
    )
    {
        switch (speakerField.ToLowerInvariant())
        {
            case "" or "user" or "viewer" or "trigger":
                return ctx.TriggeredByUserId;
            case "bot" or "channel" or "default":
                return string.Empty;
            case "broadcaster":
                return (
                    await _resolver.ResolveAsync(
                        "{{channel.id}}",
                        ctx.Variables,
                        ctx.BroadcasterId,
                        ctx.CancellationToken
                    )
                ).Trim();
            default:
                return speakerField;
        }
    }
}
