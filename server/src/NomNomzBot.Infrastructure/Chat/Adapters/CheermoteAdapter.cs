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
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat.Adapters;

/// <summary>
/// Pipeline step 50 (chat-decoration spec §0/§3.4): resolves each cheermote fragment (prefix + bits) to its tier image
/// via the cached Helix cheermotes and fills <see cref="ChatMessageFragment.CheermoteImage"/> in place. Best-effort and
/// cache-only — a cheermote with no cached set is left with its raw prefix/bits and a null image (the client falls back).
/// </summary>
public sealed class CheermoteAdapter : IChatDecorationAdapter
{
    private readonly ICheermoteResolver _resolver;

    public CheermoteAdapter(ICheermoteResolver resolver)
    {
        _resolver = resolver;
    }

    public int Order => 50;

    public bool AppliesTo(ChatDecorationContext context) =>
        context.Fragments.Any(fragment =>
            fragment.Type == "cheermote" && !string.IsNullOrEmpty(fragment.CheermotePrefix)
        );

    public async Task DecorateAsync(ChatDecorationContext context, CancellationToken ct = default)
    {
        foreach (ChatMessageFragment fragment in context.Fragments)
        {
            if (fragment.Type != "cheermote" || string.IsNullOrEmpty(fragment.CheermotePrefix))
                continue;

            fragment.CheermoteImage = await _resolver.ResolveAsync(
                context.BroadcasterId,
                fragment.CheermotePrefix,
                fragment.CheermoteBits ?? 0,
                fragment.CheermoteTier ?? 1,
                ct
            );
        }
    }
}
