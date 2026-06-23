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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat.Adapters;

/// <summary>
/// Pipeline step 70 (chat-decoration spec §0/§3.5): turns a standalone url word into a <c>link</c> fragment and attaches
/// its OpenGraph preview. Gated — it runs only when the channel has opted in (<c>use_link_preview</c>) AND the sender has
/// the required standing (subscriber and above) — so an arbitrary viewer's link never triggers an outbound fetch. The
/// fetch itself is SSRF-hardened and cached in <see cref="ILinkPreviewService"/>; a miss leaves the link without a preview.
/// </summary>
public sealed class LinkPreviewAdapter : IChatDecorationAdapter
{
    private readonly ILinkPreviewService _previews;

    public LinkPreviewAdapter(ILinkPreviewService previews)
    {
        _previews = previews;
    }

    public int Order => 70;

    public bool AppliesTo(ChatDecorationContext context) =>
        context.SenderHasPreviewStanding
        && context.EnabledFeatures.Contains("use_link_preview")
        && context.Fragments.Any(IsHttpUrl);

    public async Task DecorateAsync(ChatDecorationContext context, CancellationToken ct = default)
    {
        for (int i = 0; i < context.Fragments.Count; i++)
        {
            ChatMessageFragment fragment = context.Fragments[i];
            if (
                !IsHttpUrl(fragment)
                || !Uri.TryCreate(fragment.Text, UriKind.Absolute, out Uri? url)
            )
                continue;

            Result<LinkPreview?> preview = await _previews.FetchAsync(url, ct);
            context.Fragments[i] = new ChatMessageFragment
            {
                Type = "link",
                Text = fragment.Text,
                LinkUrl = fragment.Text,
                LinkPreview = preview.IsSuccess ? preview.Value : null,
            };
        }
    }

    private static bool IsHttpUrl(ChatMessageFragment fragment) =>
        fragment.Type == "text"
        && !string.IsNullOrWhiteSpace(fragment.Text)
        && Uri.TryCreate(fragment.Text, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
