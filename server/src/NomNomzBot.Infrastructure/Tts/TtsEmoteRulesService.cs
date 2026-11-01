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

using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Tts.Entities;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>See <see cref="ITtsEmoteRulesService"/>. The channel's emote list is read from Twitch and cached for ten minutes.</summary>
public sealed class TtsEmoteRulesService : ITtsEmoteRulesService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private readonly ITwitchChatAssetsApi _assets;
    private readonly ISpokenNameFormatter _formatter;
    private readonly ICacheService _cache;

    public TtsEmoteRulesService(
        ITwitchChatAssetsApi assets,
        ISpokenNameFormatter formatter,
        ICacheService cache
    )
    {
        _assets = assets;
        _formatter = formatter;
        _cache = cache;
    }

    private static string CacheKey(Guid broadcasterId) => $"tts:emote-rules:{broadcasterId}";

    public async Task<IReadOnlyList<TtsLexiconEntryDto>> RulesAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        List<TtsLexiconEntryDto>? cached = await _cache.GetAsync<List<TtsLexiconEntryDto>>(
            CacheKey(broadcasterId),
            cancellationToken
        );
        if (cached is not null)
            return cached;

        Result<IReadOnlyList<TwitchChannelEmote>> found = await _assets.GetChannelEmotesAsync(
            broadcasterId,
            cancellationToken
        );
        // A failed read is not remembered: the next utterance asks again instead of keeping a gap for ten minutes.
        if (found.IsFailure)
            return [];

        List<string> codes = [.. found.Value.Select(e => e.Name).Distinct(StringComparer.Ordinal)];
        List<TtsLexiconEntryDto> rules =
        [
            .. TtsEmoteSpokenNames
                .Derive(codes, _formatter.Format)
                .Select(spoken => new TtsLexiconEntryDto(
                    Guid.Empty,
                    spoken.Key,
                    spoken.Value,
                    TtsLexiconMatchKinds.Word
                )),
        ];
        await _cache.SetAsync(CacheKey(broadcasterId), rules, CacheTtl, cancellationToken);

        return rules;
    }
}
