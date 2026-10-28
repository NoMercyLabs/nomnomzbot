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

using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;

namespace NomNomzBot.Infrastructure.Chat.Adapters;

/// <summary>
/// Pipeline step 40 (chat-decoration spec §0/§3.3): resolves the message's badges to render-ready image urls via the
/// cached Helix badge sets and writes them to <see cref="ChatDecorationContext.ResolvedBadges"/>. Best-effort and
/// cache-only — an un-warmed badge comes through with empty urls rather than blocking the message.
/// </summary>
public sealed class BadgeAdapter : IChatDecorationAdapter
{
    private readonly IChatBadgeResolver _resolver;

    public BadgeAdapter(IChatBadgeResolver resolver)
    {
        _resolver = resolver;
    }

    public int Order => 40;

    public bool AppliesTo(ChatDecorationContext context) => context.Badges.Count > 0;

    public async Task DecorateAsync(ChatDecorationContext context, CancellationToken ct = default)
    {
        context.ResolvedBadges = await _resolver.ResolveAsync(
            context.BroadcasterId,
            context.Badges,
            ct
        );
    }
}
