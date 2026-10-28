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
