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

using NomNomzBot.Domain.Chat.Enums;

namespace NomNomzBot.Application.Chat.Services;

/// <summary>
/// Indexes the discovered <see cref="IThirdPartyEmoteProvider"/> adapters by their
/// <see cref="EmoteProvider"/>, so the decorator and refresh worker resolve a provider without a switch
/// (chat-decoration spec §3.2).
/// </summary>
public interface IThirdPartyEmoteProviderRegistry
{
    /// <summary>All registered third-party emote providers.</summary>
    IReadOnlyCollection<IThirdPartyEmoteProvider> All { get; }

    /// <summary>The adapter for a provider, or null when that provider has no registered adapter.</summary>
    IThirdPartyEmoteProvider? Get(EmoteProvider provider);
}
