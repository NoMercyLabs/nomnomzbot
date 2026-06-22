// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
