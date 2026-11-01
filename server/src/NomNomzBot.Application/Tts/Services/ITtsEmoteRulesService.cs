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

using NomNomzBot.Application.Tts.Dtos;

namespace NomNomzBot.Application.Tts.Services;

/// <summary>
/// The pronunciation rules a channel's own Twitch emotes give the voice: the name of the emote without the channel
/// prefix, cleaned like a viewer name (camel case split, leetspeak decoded). Read from the channel's emote list, so
/// the streamer writes none of them. The lexicon applies them after the streamer's own rules, which win.
/// </summary>
public interface ITtsEmoteRulesService
{
    /// <summary>
    /// The rules for the channel, empty when its emotes cannot be read or do not agree on one prefix (the emotes are
    /// then spoken as written).
    /// </summary>
    Task<IReadOnlyList<TtsLexiconEntryDto>> RulesAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );
}
