// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.Enums;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// A third-party emote network (BTTV / FFZ / 7TV). Each provider is an adapter that fetches its global and
/// per-channel emote sets and maps them to the unified <see cref="ChatEmote"/> shape (chat-decoration spec §3.2).
/// Adding a provider = one new impl; it self-registers and is resolved through
/// <see cref="IThirdPartyEmoteProviderRegistry"/> — never a switch.
/// </summary>
public interface IThirdPartyEmoteProvider
{
    /// <summary>Which network this adapter serves.</summary>
    EmoteProvider Provider { get; }

    /// <summary>The network's global emote set.</summary>
    Task<Result<IReadOnlyList<ChatEmote>>> GetGlobalAsync(CancellationToken ct = default);

    /// <summary>
    /// A channel's emote set. The lookup key is provider-specific — FFZ uses the broadcaster LOGIN, BTTV and 7TV
    /// use the Twitch broadcaster id — so both are passed and each adapter uses the one it needs.
    /// </summary>
    Task<Result<IReadOnlyList<ChatEmote>>> GetChannelAsync(
        string twitchBroadcasterId,
        string broadcasterLogin,
        CancellationToken ct = default
    );
}
