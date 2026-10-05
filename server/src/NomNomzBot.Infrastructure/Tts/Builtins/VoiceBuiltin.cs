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
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Common.Picking;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Infrastructure.Commands.Builtins;

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
    private const string FeatureDisabledCode = "FEATURE_DISABLED";

    /// <summary>The most voice ids one "Multiple matches" reply lists (old-bot cap).</summary>
    private const int MaxListedMatches = 10;

    private readonly ITtsConfigService _tts;
    private readonly IBuiltinResponseComposer _composer;

    public VoiceBuiltin(ITtsConfigService tts, IBuiltinResponseComposer composer)
    {
        _tts = tts;
        _composer = composer;
    }

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

        if (args.Length == 0)
            return await ReplyAsync(
                context,
                BuiltinResponseSlots.Voice.Usage,
                "Voice commands: !voice languages | !voice get <language> | !voice set <name> | !voice current | !voice roulette",
                null,
                ct
            );

        string[] parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string head = parts[0].ToLowerInvariant();
        string rest = parts.Length > 1 ? string.Join(' ', parts[1..]).Trim() : string.Empty;

        // Subcommands first; anything else stays the bare fuzzy search, so `!voice british female` keeps
        // working without a subcommand.
        return head switch
        {
            "clear" or "reset" or "default" => await ClearAsync(context, ct),
            "current" => await ShowAsync(context, ct),
            "languages" or "langs" => await LanguagesAsync(context, ct),
            "get" => rest.Length == 0
                ? await ReplyAsync(
                    context,
                    BuiltinResponseSlots.Voice.GetUsage,
                    "Usage: !voice get <language> (e.g. !voice get en or !voice get en-US)",
                    null,
                    ct
                )
                : await VoicesForLanguageAsync(context, rest, ct),
            "set" => rest.Length == 0
                ? await ReplyAsync(
                    context,
                    BuiltinResponseSlots.Voice.SetUsage,
                    "Usage: !voice set <name> (e.g. !voice set Ana, !voice set en-US-AnaNeural)",
                    null,
                    ct
                )
                : await SetAsync(context, rest, bare: false, ct),
            "roulette" => await RouletteAsync(context, ct),
            _ => await SetAsync(context, args, bare: true, ct),
        };
    }

    /// <summary>Every language the catalogue can speak, grouped by language code (<c>EN: en-US, en-GB</c>).</summary>
    private async Task<Result<string>> LanguagesAsync(
        BuiltinCommandContext context,
        CancellationToken ct
    )
    {
        IReadOnlyList<TtsVoiceDto> catalogue = await AllVoicesAsync(ct);
        if (catalogue.Count == 0)
            return await NoVoicesAsync(context, ct);

        IEnumerable<IGrouping<string, string>> groups = catalogue
            .Select(v => v.Locale)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(locale => locale, StringComparer.OrdinalIgnoreCase)
            .GroupBy(
                locale => locale.Split('-')[0].ToUpperInvariant(),
                StringComparer.OrdinalIgnoreCase
            )
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        string list = string.Join(" | ", groups.Select(g => $"{g.Key}: {string.Join(", ", g)}"));
        return await ReplyAsync(
            context,
            BuiltinResponseSlots.Voice.Languages,
            "Available languages: {voice.languages}",
            Vars(("voice.languages", list)),
            ct
        );
    }

    /// <summary>
    /// The voices for one language. Accepts a bare language code (<c>en</c>) or a full locale (<c>en-US</c>);
    /// a bare code matches every locale under it, which is what a viewer means by "English".
    /// </summary>
    private async Task<Result<string>> VoicesForLanguageAsync(
        BuiltinCommandContext context,
        string language,
        CancellationToken ct
    )
    {
        IReadOnlyList<TtsVoiceDto> catalogue = await AllVoicesAsync(ct);
        if (catalogue.Count == 0)
            return await NoVoicesAsync(context, ct);

        string query = language.Trim();
        List<TtsVoiceDto> matches =
        [
            .. catalogue.Where(v =>
                query.Contains('-')
                    ? string.Equals(v.Locale, query, StringComparison.OrdinalIgnoreCase)
                    : v.Locale.StartsWith(query + "-", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(v.Locale, query, StringComparison.OrdinalIgnoreCase)
            ),
        ];
        if (matches.Count == 0)
            return await ReplyAsync(
                context,
                BuiltinResponseSlots.Voice.NoVoicesForLanguage,
                "No voices found for '{voice.language}'. Try !voice languages",
                Vars(("voice.language", query)),
                ct
            );

        // Chat is one line: name the first handful and say how many more there are, rather than truncating
        // mid-list and leaving the viewer thinking that is all of them.
        const int shown = 12;
        string names = string.Join(", ", matches.Take(shown).Select(SpeakerName));
        return matches.Count > shown
            ? await ReplyAsync(
                context,
                BuiltinResponseSlots.Voice.VoicesMore,
                "{voice.language} voices: {voice.list} (+{voice.more} more). Pick one with !voice set <name>.",
                Vars(
                    ("voice.language", query.ToUpperInvariant()),
                    ("voice.list", names),
                    ("voice.more", (matches.Count - shown).ToString())
                ),
                ct
            )
            : await ReplyAsync(
                context,
                BuiltinResponseSlots.Voice.Voices,
                "{voice.language} voices: {voice.list}",
                Vars(("voice.language", query.ToUpperInvariant()), ("voice.list", names)),
                ct
            );
    }

    /// <summary>Picks a random catalogue voice and keeps it - the pick is saved, not a one-off.</summary>
    private async Task<Result<string>> RouletteAsync(
        BuiltinCommandContext context,
        CancellationToken ct
    )
    {
        IReadOnlyList<TtsVoiceDto> catalogue = await AllVoicesAsync(ct);
        if (catalogue.Count == 0)
            return await ReplyAsync(
                context,
                BuiltinResponseSlots.Voice.NoVoicesForRoulette,
                "No voices available for roulette!",
                null,
                ct
            );

        TtsVoiceDto pick = NoImmediateRepeatPicker.Pick(
            catalogue,
            $"tts.roulette:{context.BroadcasterId}:{context.TriggeringUserId}"
        );
        Result<UserTtsVoiceDto> set = await _tts.SetOwnVoiceAsync(
            context.BroadcasterId,
            context.TriggeringUserId,
            new() { VoiceId = pick.Id },
            ct
        );
        if (set.IsFailure)
            return await SetFailedAsync(context, set, ct);

        return await ReplyAsync(
            context,
            BuiltinResponseSlots.Voice.Roulette,
            "The wheel has spoken! Your next TTS message will be in... {voice.name} ({voice.locale}). Good luck.",
            PickVars(pick),
            ct
        );
    }

    /// <summary>
    /// The WHOLE catalogue, walked page by page through the search API's own paging contract. Asking for one
    /// huge page does not work: <c>SearchVoicesAsync</c> clamps PageSize to 200 and the live catalogue is
    /// larger, so a single call silently returns a truncated list and `!voice languages` would quietly omit
    /// whole languages. The walk follows <see cref="PagedList{T}.HasNextPage"/> until the server says there is
    /// no more, and is bounded so a server that never stops saying "more" cannot spin forever.
    /// </summary>
    private async Task<IReadOnlyList<TtsVoiceDto>> AllVoicesAsync(CancellationToken ct)
    {
        const int pageSize = 200;
        const int maxPages = 50;

        List<TtsVoiceDto> voices = [];
        for (int page = 1; page <= maxPages; page++)
        {
            Result<PagedList<TtsVoiceDto>> result = await _tts.SearchVoicesAsync(
                new(Page: page, PageSize: pageSize),
                ct
            );
            if (result.IsFailure)
                break;

            voices.AddRange(result.Value.Items);
            if (!result.Value.HasNextPage)
                break;
        }
        return voices;
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
        BuiltinCommandContext context,
        CancellationToken ct
    )
    {
        Result<UserTtsVoiceDto?> own = await _tts.GetOwnVoiceAsync(
            context.BroadcasterId,
            context.TriggeringUserId,
            ct
        );
        if (own is { IsSuccess: true, Value: { } voice })
            return await ReplyAsync(
                context,
                BuiltinResponseSlots.Voice.Current,
                "Current voice: {voice.id}",
                Vars(("voice.id", voice.VoiceId)),
                ct
            );
        IReadOnlyList<TtsVoiceDto> catalogue = await AllVoicesAsync(ct);
        TtsVoiceDto? channelDefault = catalogue.FirstOrDefault(v => v.IsDefault);
        return channelDefault is null
            ? await ReplyAsync(
                context,
                BuiltinResponseSlots.Voice.CurrentNone,
                "No voice set. Use !voice get <language> to find voices.",
                null,
                ct
            )
            : await ReplyAsync(
                context,
                BuiltinResponseSlots.Voice.CurrentDefault,
                "Using default: {voice.name}. Set custom voice with !voice set <name>",
                Vars(("voice.name", channelDefault.DisplayName)),
                ct
            );
    }

    private async Task<Result<string>> ClearAsync(
        BuiltinCommandContext context,
        CancellationToken ct
    )
    {
        Result cleared = await _tts.ClearOwnVoiceAsync(
            context.BroadcasterId,
            context.TriggeringUserId,
            ct
        );
        if (cleared.IsSuccess)
            return await ReplyAsync(
                context,
                BuiltinResponseSlots.Voice.Cleared,
                "Your TTS voice is back to the channel default.",
                null,
                ct
            );

        return cleared.ErrorCode == FeatureDisabledCode
            ? await DisabledAsync(context, ct)
            : await ReplyAsync(
                context,
                BuiltinResponseSlots.Voice.ClearFailed,
                "I couldn't reset your voice.",
                null,
                ct
            );
    }

    private async Task<Result<string>> SetAsync(
        BuiltinCommandContext context,
        string query,
        bool bare,
        CancellationToken ct
    )
    {
        Result<PagedList<TtsVoiceDto>> matches = await _tts.SearchVoicesAsync(
            new(Q: query, PageSize: 10),
            ct
        );
        if (matches.IsFailure || matches.Value.Items.Count == 0)
            return bare
                ? await ReplyAsync(
                    context,
                    BuiltinResponseSlots.Voice.UnknownCommand,
                    "Unknown voice command. Use: !voice languages | !voice get <language> | !voice set <name> | !voice current | !voice roulette",
                    null,
                    ct
                )
                : await ReplyAsync(
                    context,
                    BuiltinResponseSlots.Voice.NoMatch,
                    "Voice '{query}' not found. Use !voice get <language> to see available voices.",
                    Vars(("query", query)),
                    ct
                );

        IReadOnlyList<TtsVoiceDto> candidates = Candidates(matches.Value.Items, query);
        if (candidates.Count > 1)
        {
            string list = string.Join(
                ", ",
                candidates
                    .Select(v => v.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .Take(MaxListedMatches)
            );
            return await ReplyAsync(
                context,
                BuiltinResponseSlots.Voice.MultipleMatches,
                "Multiple matches: {voice.list}",
                Vars(("voice.list", list)),
                ct
            );
        }

        TtsVoiceDto pick = candidates[0];
        Result<UserTtsVoiceDto> set = await _tts.SetOwnVoiceAsync(
            context.BroadcasterId,
            context.TriggeringUserId,
            new() { VoiceId = pick.Id },
            ct
        );
        if (set.IsFailure)
            return await SetFailedAsync(context, set, ct);

        int total = matches.Value.TotalCount;
        return total > 1
            ? await ReplyAsync(
                context,
                BuiltinResponseSlots.Voice.SetMany,
                "Your TTS voice is now {voice.name} [{voice.locale} {voice.gender}]. ({voice.count} matched — add a word to narrow it, or !voice clear to reset.)",
                PickVars(pick, total),
                ct
            )
            : await ReplyAsync(
                context,
                BuiltinResponseSlots.Voice.Set,
                "✅ Voice set to {voice.name}!",
                PickVars(pick),
                ct
            );
    }

    /// <summary>
    /// A failed set: FEATURE_DISABLED (self-service locked) gets the disabled slot; anything else (for example
    /// NOT_FOUND when the voice vanished) the generic failure slot. The service message stays for logs.
    /// </summary>
    private Task<Result<string>> SetFailedAsync(
        BuiltinCommandContext context,
        Result failure,
        CancellationToken ct
    ) =>
        failure.ErrorCode == FeatureDisabledCode
            ? DisabledAsync(context, ct)
            : ReplyAsync(
                context,
                BuiltinResponseSlots.Voice.SetFailed,
                "I couldn't set that voice.",
                null,
                ct
            );

    private Task<Result<string>> DisabledAsync(
        BuiltinCommandContext context,
        CancellationToken ct
    ) =>
        ReplyAsync(
            context,
            BuiltinResponseSlots.Voice.Disabled,
            "Picking your own voice is turned off on this channel.",
            null,
            ct
        );

    private Task<Result<string>> NoVoicesAsync(
        BuiltinCommandContext context,
        CancellationToken ct
    ) =>
        ReplyAsync(
            context,
            BuiltinResponseSlots.Voice.NoVoices,
            "No TTS voices available.",
            null,
            ct
        );

    private async Task<Result<string>> ReplyAsync(
        BuiltinCommandContext context,
        string slot,
        string neutralFallback,
        IReadOnlyDictionary<string, string>? variables,
        CancellationToken ct
    ) =>
        Result.Success(
            await _composer.ComposeAsync(
                context,
                BuiltinResponseSlots.Voice.Key,
                slot,
                neutralFallback,
                variables,
                ct
            )
        );

    private static Dictionary<string, string> Vars(params (string Name, string Value)[] values) =>
        new(
            values.Select(v => KeyValuePair.Create(v.Name, v.Value)),
            StringComparer.OrdinalIgnoreCase
        );

    private static Dictionary<string, string> PickVars(TtsVoiceDto pick, int? matched = null)
    {
        Dictionary<string, string> vars = Vars(
            ("voice.name", pick.DisplayName),
            ("voice.locale", pick.Locale),
            ("voice.gender", pick.Gender)
        );
        if (matched is { } count)
            vars["voice.count"] = count.ToString();
        return vars;
    }

    // Relevance beats catalogue order. The rung that matters most in chat is the BARE SPEAKER NAME: a viewer
    // types `!voice set Ana` meaning en-US-AnaNeural, and substring relevance alone hands them ar-IQ-RanaNeural
    // because it sorts first in the catalogue. Exact id, then exact name/display-name, then the speaker name
    // (en-US-AnaNeural → "Ana"), then a speaker-name prefix, and only then every substring hit. The first rung
    // that matches wins; more than one voice on that rung means the name is ambiguous and the caller lists
    // them (old-bot "Multiple matches") instead of silently picking one.
    private static IReadOnlyList<TtsVoiceDto> Candidates(
        IReadOnlyList<TtsVoiceDto> voices,
        string query
    )
    {
        string q = query.Trim();
        TtsVoiceDto? exactId = voices.FirstOrDefault(v =>
            string.Equals(v.Id, q, StringComparison.OrdinalIgnoreCase)
        );
        if (exactId is not null)
            return [exactId];

        Func<TtsVoiceDto, bool>[] rungs =
        [
            v => string.Equals(v.Name, q, StringComparison.OrdinalIgnoreCase),
            v => string.Equals(v.DisplayName, q, StringComparison.OrdinalIgnoreCase),
            v => string.Equals(SpeakerName(v), q, StringComparison.OrdinalIgnoreCase),
            v => SpeakerName(v).StartsWith(q, StringComparison.OrdinalIgnoreCase),
        ];
        foreach (Func<TtsVoiceDto, bool> rung in rungs)
        {
            List<TtsVoiceDto> hits = voices.Where(rung).ToList();
            if (hits.Count > 0)
                return hits;
        }
        return voices;
    }
}
