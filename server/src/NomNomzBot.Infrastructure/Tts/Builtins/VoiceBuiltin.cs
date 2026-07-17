// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;

namespace NomNomzBot.Infrastructure.Tts.Builtins;

/// <summary>
/// <c>!voice</c> — the viewer self-service voice picker in chat (tts.md §6.1). Each viewer owns their own TTS
/// voice; the channel default reads for everyone who hasn't picked one (Firebot's model). Keyed by the caller's
/// platform user id — exactly what the dispatch voice-resolver reads — so a pick here takes effect on the next
/// utterance. The channel gate (TTS enabled + <c>ViewerVoiceSelfServiceEnabled</c>) lives in the service, so a
/// streamer who locks it off gets a friendly refusal, not a silent no-op.
/// <list type="bullet">
///   <item><c>!voice</c> → shows the caller's current voice (or that they use the channel default).</item>
///   <item><c>!voice &lt;search&gt;</c> → fuzzy-matches the catalogue (name/accent/language/tags) and sets it.</item>
///   <item><c>!voice clear|reset|default</c> → drops back to the channel default.</item>
/// </list>
/// A non-reserved built-in — the channel may disable the command entirely, independent of the config toggle.
/// </summary>
public sealed class VoiceBuiltin : IBuiltinCommand
{
    private readonly ITtsConfigService _tts;

    public VoiceBuiltin(ITtsConfigService tts) => _tts = tts;

    public string BuiltinKey => "voice";
    public int DefaultCooldownSeconds => 5;

    // Everyone may run it; the real gate (TTS enabled + viewer self-service allowed) is enforced in the service.
    public int DefaultMinPermissionLevel => 0;

    public async Task<Result<string>> ExecuteAsync(
        BuiltinCommandContext context,
        CancellationToken ct = default
    )
    {
        string args = context.Args.Trim();
        string viewerId = context.TriggeringUserId;

        if (args.Length == 0)
            return await ShowAsync(context.BroadcasterId, viewerId, ct);

        if (args.ToLowerInvariant() is "clear" or "reset" or "default")
            return await ClearAsync(context.BroadcasterId, viewerId, ct);

        return await SetAsync(context.BroadcasterId, viewerId, args, ct);
    }

    private async Task<Result<string>> ShowAsync(
        Guid broadcasterId,
        string viewerId,
        CancellationToken ct
    )
    {
        Result<UserTtsVoiceDto?> own = await _tts.GetOwnVoiceAsync(broadcasterId, viewerId, ct);
        if (own.IsSuccess && own.Value is UserTtsVoiceDto voice)
            return Result.Success(
                $"Your TTS voice is {voice.VoiceId}. Change it with !voice <search>, or !voice clear to use the channel default."
            );
        return Result.Success(
            "You're using the channel default TTS voice. Pick your own with !voice <search> — e.g. !voice british female."
        );
    }

    private async Task<Result<string>> ClearAsync(
        Guid broadcasterId,
        string viewerId,
        CancellationToken ct
    )
    {
        Result cleared = await _tts.ClearOwnVoiceAsync(broadcasterId, viewerId, ct);
        // A gate refusal (FEATURE_DISABLED) carries a viewer-friendly message; surface it verbatim.
        return Result.Success(
            cleared.IsSuccess
                ? "Your TTS voice is back to the channel default."
                : cleared.ErrorMessage ?? "I couldn't reset your voice."
        );
    }

    private async Task<Result<string>> SetAsync(
        Guid broadcasterId,
        string viewerId,
        string query,
        CancellationToken ct
    )
    {
        Result<PagedList<TtsVoiceDto>> matches = await _tts.SearchVoicesAsync(
            new TtsVoiceQuery(Q: query, PageSize: 10),
            ct
        );
        if (matches.IsFailure || matches.Value.Items.Count == 0)
            return Result.Success(
                $"No voice matched \"{query}\". Try a name, a language like en-US, or an accent like british."
            );

        TtsVoiceDto pick = BestMatch(matches.Value.Items, query);
        Result<UserTtsVoiceDto> set = await _tts.SetOwnVoiceAsync(
            broadcasterId,
            viewerId,
            new SetUserVoiceDto { VoiceId = pick.Id },
            ct
        );
        if (set.IsFailure)
            // FEATURE_DISABLED (self-service locked) or NOT_FOUND (voice vanished) — reply with the reason.
            return Result.Success(set.ErrorMessage ?? "I couldn't set that voice.");

        int total = matches.Value.TotalCount;
        string extra =
            total > 1
                ? $" ({total} matched — add a word to narrow it, or !voice clear to reset.)"
                : "";
        return Result.Success(
            $"Your TTS voice is now {pick.DisplayName} [{pick.Locale} {pick.Gender}].{extra}"
        );
    }

    // Relevance beats catalogue order: an exact voice-id, then an exact name/display-name hit, wins the top
    // page slot; otherwise the first catalogue-ordered match (provider→locale→name) is a reasonable default.
    private static TtsVoiceDto BestMatch(IReadOnlyList<TtsVoiceDto> voices, string query)
    {
        string q = query.Trim();
        return voices.FirstOrDefault(v =>
                string.Equals(v.Id, q, StringComparison.OrdinalIgnoreCase)
            )
            ?? voices.FirstOrDefault(v =>
                string.Equals(v.Name, q, StringComparison.OrdinalIgnoreCase)
            )
            ?? voices.FirstOrDefault(v =>
                string.Equals(v.DisplayName, q, StringComparison.OrdinalIgnoreCase)
            )
            ?? voices[0];
    }
}
