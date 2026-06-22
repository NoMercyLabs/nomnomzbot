// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
