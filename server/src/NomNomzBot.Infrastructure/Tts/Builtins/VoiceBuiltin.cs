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
///   <item><c>!voice</c> / <c>!voice current</c> → the caller's voice (or that they use the channel default).</item>
///   <item><c>!voice languages</c> → the languages the catalogue can speak, grouped by language code.</item>
///   <item><c>!voice get &lt;language&gt;</c> → the voices for a language (<c>en</c> or <c>en-US</c>).</item>
///   <item><c>!voice set &lt;name&gt;</c> → sets by id, name or bare speaker name (<c>Ana</c> → <c>en-US-AnaNeural</c>).</item>
///   <item><c>!voice roulette</c> → picks a random catalogue voice and keeps it.</item>
///   <item><c>!voice &lt;search&gt;</c> → the bare form still fuzzy-matches and sets, no subcommand needed.</item>
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

        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string head = parts[0].ToLowerInvariant();
        string rest = parts.Length > 1 ? string.Join(' ', parts[1..]).Trim() : string.Empty;

        // Subcommands first; anything else stays the bare fuzzy search, so `!voice british female` keeps
        // working without a subcommand.
        return head switch
        {
            "clear" or "reset" or "default" => await ClearAsync(
                context.BroadcasterId,
                viewerId,
                ct
            ),
            "current" => await ShowAsync(context.BroadcasterId, viewerId, ct),
            "languages" or "langs" => await LanguagesAsync(ct),
            "get" => rest.Length == 0
                ? Result.Success(
                    "Usage: !voice get <language> - e.g. !voice get en, or !voice get en-US."
                )
                : await VoicesForLanguageAsync(rest, ct),
            "set" => rest.Length == 0
                ? Result.Success("Usage: !voice set <name> - e.g. !voice set Ana.")
                : await SetAsync(context.BroadcasterId, viewerId, rest, ct),
            "roulette" => await RouletteAsync(context.BroadcasterId, viewerId, ct),
            _ => await SetAsync(context.BroadcasterId, viewerId, args, ct),
        };
    }

    /// <summary>Every language the catalogue can speak, grouped by language code (<c>EN: en-US, en-GB</c>).</summary>
    private async Task<Result<string>> LanguagesAsync(CancellationToken ct)
    {
        Result<PagedList<TtsVoiceDto>> all = await _tts.SearchVoicesAsync(new(PageSize: 1000), ct);
        if (all.IsFailure || all.Value.Items.Count == 0)
            return Result.Success("No TTS voices are available right now.");

        IEnumerable<IGrouping<string, string>> groups = all
            .Value.Items.Select(v => v.Locale)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(locale => locale, StringComparer.OrdinalIgnoreCase)
            .GroupBy(
                locale => locale.Split('-')[0].ToUpperInvariant(),
                StringComparer.OrdinalIgnoreCase
            )
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        string list = string.Join(" | ", groups.Select(g => $"{g.Key}: {string.Join(", ", g)}"));
        return Result.Success($"Languages: {list}");
    }

    /// <summary>
    /// The voices for one language. Accepts a bare language code (<c>en</c>) or a full locale (<c>en-US</c>);
    /// a bare code matches every locale under it, which is what a viewer means by "English".
    /// </summary>
    private async Task<Result<string>> VoicesForLanguageAsync(string language, CancellationToken ct)
    {
        Result<PagedList<TtsVoiceDto>> all = await _tts.SearchVoicesAsync(new(PageSize: 1000), ct);
        if (all.IsFailure)
            return Result.Success("I could not read the voice catalogue.");

        string query = language.Trim();
        List<TtsVoiceDto> matches =
        [
            .. all.Value.Items.Where(v =>
                query.Contains('-')
                    ? string.Equals(v.Locale, query, StringComparison.OrdinalIgnoreCase)
                    : v.Locale.StartsWith(query + "-", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(v.Locale, query, StringComparison.OrdinalIgnoreCase)
            ),
        ];
        if (matches.Count == 0)
            return Result.Success(
                $"No voices for {query}. Try !voice languages to see what is available."
            );

        // Chat is one line: name the first handful and say how many more there are, rather than truncating
        // mid-list and leaving the viewer thinking that is all of them.
        const int shown = 12;
        string names = string.Join(", ", matches.Take(shown).Select(SpeakerName));
        string more = matches.Count > shown ? $" (+{matches.Count - shown} more)" : string.Empty;
        return Result.Success($"{query} voices: {names}{more}. Pick one with !voice set <name>.");
    }

    /// <summary>Picks a random catalogue voice and keeps it - the pick is saved, not a one-off.</summary>
    private async Task<Result<string>> RouletteAsync(
        Guid broadcasterId,
        string viewerId,
        CancellationToken ct
    )
    {
        Result<PagedList<TtsVoiceDto>> all = await _tts.SearchVoicesAsync(new(PageSize: 1000), ct);
        if (all.IsFailure || all.Value.Items.Count == 0)
            return Result.Success("No voices available for roulette!");

        TtsVoiceDto pick = all.Value.Items[Random.Shared.Next(all.Value.Items.Count)];
        Result<UserTtsVoiceDto> set = await _tts.SetOwnVoiceAsync(
            broadcasterId,
            viewerId,
            new() { VoiceId = pick.Id },
            ct
        );
        if (set.IsFailure)
            return Result.Success(set.ErrorMessage ?? "I could not set that voice.");

        return Result.Success(
            $"The wheel has spoken - your voice is now {pick.DisplayName} [{pick.Locale} {pick.Gender}]. No takebacks."
        );
    }

    /// <summary>The bare speaker name a viewer would say out loud: <c>en-US-AnaNeural</c> becomes <c>Ana</c>.</summary>
    private static string SpeakerName(TtsVoiceDto voice)
    {
        string name = voice.Id;
        int lastDash = name.LastIndexOf('-');
        if (lastDash >= 0)
            name = name[(lastDash + 1)..];
        return name.EndsWith("Neural", StringComparison.OrdinalIgnoreCase)
            ? name[..^"Neural".Length]
            : name;
    }

    private async Task<Result<string>> ShowAsync(
        Guid broadcasterId,
        string viewerId,
        CancellationToken ct
    )
    {
        Result<UserTtsVoiceDto?> own = await _tts.GetOwnVoiceAsync(broadcasterId, viewerId, ct);
        if (own is { IsSuccess: true, Value: { } voice })
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
            new(Q: query, PageSize: 10),
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
            new() { VoiceId = pick.Id },
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

    // Relevance beats catalogue order. The rung that matters most in chat is the BARE SPEAKER NAME: a viewer
    // types `!voice set Ana` meaning en-US-AnaNeural, and substring relevance alone hands them ar-IQ-RanaNeural
    // because it sorts first in the catalogue. Exact id, then exact name/display-name, then the speaker name
    // (en-US-AnaNeural → "Ana"), then a speaker-name prefix, and only then catalogue order.
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
            ?? voices.FirstOrDefault(v =>
                string.Equals(SpeakerName(v), q, StringComparison.OrdinalIgnoreCase)
            )
            ?? voices.FirstOrDefault(v =>
                SpeakerName(v).StartsWith(q, StringComparison.OrdinalIgnoreCase)
            )
            ?? voices[0];
    }
}
