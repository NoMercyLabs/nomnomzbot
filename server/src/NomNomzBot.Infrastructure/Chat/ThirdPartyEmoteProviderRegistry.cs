// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.Enums;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Indexes the discovered <see cref="IThirdPartyEmoteProvider"/> adapters by <see cref="EmoteProvider"/>
/// (chat-decoration spec §3.2). The provider set is fixed at startup, so the index is built once.
/// </summary>
public sealed class ThirdPartyEmoteProviderRegistry : IThirdPartyEmoteProviderRegistry
{
    private readonly IReadOnlyCollection<IThirdPartyEmoteProvider> _all;
    private readonly IReadOnlyDictionary<EmoteProvider, IThirdPartyEmoteProvider> _byProvider;

    public ThirdPartyEmoteProviderRegistry(IEnumerable<IThirdPartyEmoteProvider> providers)
    {
        List<IThirdPartyEmoteProvider> list = [.. providers];
        _all = list;
        _byProvider = list.ToDictionary(provider => provider.Provider);
    }

    public IReadOnlyCollection<IThirdPartyEmoteProvider> All => _all;

    public IThirdPartyEmoteProvider? Get(EmoteProvider provider) =>
        _byProvider.TryGetValue(provider, out IThirdPartyEmoteProvider? found) ? found : null;
}
