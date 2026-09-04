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
/// Pipeline step 70 (chat-decoration spec §0/§3.5): turns a url into a <c>link</c> fragment and attaches its OpenGraph
/// preview. Gated — it runs only when the channel has opted in (<c>use_link_preview</c>) AND the sender has the required
/// standing (subscriber and above) — so an arbitrary viewer's link never triggers an outbound fetch. The fetch itself is
/// SSRF-hardened and cached in <see cref="ILinkPreviewService"/>; a miss leaves the link without a preview.
///
/// <para>
/// The url does NOT have to be the whole fragment. Twitch splits fragments around emotes and mentions, never around
/// urls, so <c>!sr https://open.spotify.com/track/…</c> arrives as ONE text fragment. Matching only fragments that
/// parse whole as a <see cref="Uri"/> meant every url with a word in front of it — a song request, "look at this
/// {url}", any sentence — silently got no preview, while a url pasted entirely alone did. A fragment is therefore
/// SPLIT into its text and link parts here.
/// </para>
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
        && context.Fragments.Any(ContainsHttpUrl);

    public async Task DecorateAsync(ChatDecorationContext context, CancellationToken ct = default)
    {
        // Rebuilt rather than edited in place: one text fragment can become up to three (lead text, the link,
        // trailing text), so indices shift as we go.
        List<ChatMessageFragment> rebuilt = new(context.Fragments.Count);

        foreach (ChatMessageFragment fragment in context.Fragments)
        {
            if (!ContainsHttpUrl(fragment))
            {
                rebuilt.Add(fragment);
                continue;
            }

            foreach (string part in SplitAroundUrls(fragment.Text))
            {
                if (!IsHttpUrl(part) || !Uri.TryCreate(part, UriKind.Absolute, out Uri? url))
                {
                    rebuilt.Add(new() { Type = "text", Text = part });
                    continue;
                }

                Result<LinkPreview?> preview = await _previews.FetchAsync(url, ct);
                rebuilt.Add(
                    new()
                    {
                        Type = "link",
                        Text = part,
                        LinkUrl = part,
                        LinkPreview = preview.IsSuccess ? preview.Value : null,
                    }
                );
            }
        }

        context.Fragments.Clear();
        foreach (ChatMessageFragment fragment in rebuilt)
            context.Fragments.Add(fragment);
    }

    /// <summary>
    /// Splits [text] into alternating verbatim-text and url parts.
    ///
    /// <para>
    /// Every non-url part is a straight slice of the original, so concatenating the parts back together
    /// reproduces [text] byte for byte — including its spacing. The overlay renders these fragments back to
    /// back, so a splitter that rebuilt the text from words would quietly reflow or eat spaces in every
    /// message carrying a link.
    /// </para>
    /// </summary>
    private static IEnumerable<string> SplitAroundUrls(string text)
    {
        int emitted = 0; // everything before this index has already been yielded
        int index = 0;

        while (index < text.Length)
        {
            int end = text.IndexOf(' ', index);
            if (end < 0)
                end = text.Length;

            string word = text[index..end];
            if (IsHttpUrl(word))
            {
                if (index > emitted)
                    yield return text[emitted..index];

                yield return word;
                emitted = end;
            }

            index = end + 1;
        }

        if (emitted < text.Length)
            yield return text[emitted..];
    }

    private static bool ContainsHttpUrl(ChatMessageFragment fragment) =>
        fragment.Type == "text"
        && !string.IsNullOrWhiteSpace(fragment.Text)
        && fragment.Text.Split(' ').Any(IsHttpUrl);

    private static bool IsHttpUrl(string word) =>
        Uri.TryCreate(word, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
