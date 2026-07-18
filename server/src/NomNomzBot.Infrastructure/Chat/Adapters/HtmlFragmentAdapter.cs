// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using NomNomzBot.Application.Chat.Decoration;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Domain.Chat.ValueObjects;

namespace NomNomzBot.Infrastructure.Chat.Adapters;

/// <summary>
/// Pipeline step 90: renders a subscriber-and-above sender's inline HTML as a sanitised <c>html</c> fragment (the legacy
/// bot's chat-HTML behaviour). It is opt-in per channel (<c>use_chat_html</c>, default off) and gated on sender standing
/// (<see cref="ChatDecorationContext.SenderHasPreviewStanding"/>). Running after <c>ImplodeTextAdapter</c> (step 80) — so
/// text runs are coalesced and emotes already resolved — it first looks for an HTML tag that <b>spans</b> several
/// fragments (e.g. <c>&lt;marquee&gt;</c> wrapping emotes): it collects the span, and when it contains at least one emote
/// it stitches the run into one HTML string (text kept raw, each emote emitted as an <c>&lt;img&gt;</c>). Otherwise it
/// converts each single <c>text</c> fragment that is real HTML on its own. Either way the built HTML passes through
/// <see cref="ChatHtmlSanitizer"/> before it becomes the fragment's text — nothing unsanitised is ever emitted.
/// </summary>
public sealed class HtmlFragmentAdapter : IChatDecorationAdapter
{
    public int Order => 90;

    public bool AppliesTo(ChatDecorationContext context) =>
        context.EnabledFeatures.Contains("use_chat_html")
        && context.SenderHasPreviewStanding
        && context.Fragments.Any(fragment =>
            fragment.Type == "text" && fragment.Text.Contains('<') && fragment.Text.Contains('>')
        );

    public Task DecorateAsync(ChatDecorationContext context, CancellationToken ct = default)
    {
        // A span crossing fragments (text + emotes) takes precedence and rebuilds the whole list; only if none is found
        // do we fall back to converting standalone HTML text fragments — mirrors the legacy two-phase order.
        if (TryReplaceMultiFragmentSpan(context))
            return Task.CompletedTask;

        ConvertStandaloneHtmlFragments(context);
        return Task.CompletedTask;
    }

    // Rebuilds the fragment list, collapsing each opening-tag..closing-tag span that carries an emote into one sanitised
    // html fragment. Returns false (leaving the list untouched) when no such span exists.
    private static bool TryReplaceMultiFragmentSpan(ChatDecorationContext context)
    {
        List<ChatMessageFragment> fragments = context.Fragments;
        List<ChatMessageFragment> rebuilt = new(fragments.Count);
        bool anySpan = false;
        int i = 0;

        while (i < fragments.Count)
        {
            ChatMessageFragment fragment = fragments[i];

            if (fragment.Type != "text" || !HasOpeningHtmlTag(fragment.Text))
            {
                rebuilt.Add(fragment);
                i++;
                continue;
            }

            (List<ChatMessageFragment> span, int nextIndex) = CollectHtmlSpan(fragments, i);

            if (!span.Any(candidate => candidate.Type == "emote" && candidate.Emote is not null))
            {
                rebuilt.Add(fragment);
                i++;
                continue;
            }

            string sanitised = ChatHtmlSanitizer.Sanitize(BuildCombinedHtml(span));
            rebuilt.Add(new ChatMessageFragment { Type = "html", Text = sanitised });
            anySpan = true;
            i = nextIndex;
        }

        if (!anySpan)
            return false;

        context.Fragments.Clear();
        context.Fragments.AddRange(rebuilt);
        return true;
    }

    // Collects fragments from the opening-tag fragment forward up to and including the first text fragment that closes it
    // (or to the end if none does). Returns the span plus the index of the first fragment after it.
    private static (List<ChatMessageFragment> Span, int NextIndex) CollectHtmlSpan(
        List<ChatMessageFragment> fragments,
        int startIndex
    )
    {
        List<ChatMessageFragment> span = [fragments[startIndex]];
        bool foundClose = HasClosingHtmlTag(fragments[startIndex].Text);
        int j = startIndex + 1;

        while (j < fragments.Count && !foundClose)
        {
            span.Add(fragments[j]);
            if (fragments[j].Type == "text" && HasClosingHtmlTag(fragments[j].Text))
                foundClose = true;
            j++;
        }

        return (span, j);
    }

    // Concatenates a span into one HTML string: text kept verbatim (it carries the tags), each emote emitted as an <img>
    // to its best-scale url with an HTML-encoded alt; an emote with no url degrades to its encoded code (never a raw tag).
    private static string BuildCombinedHtml(IReadOnlyList<ChatMessageFragment> span)
    {
        StringBuilder html = new();

        foreach (ChatMessageFragment fragment in span)
        {
            if (fragment.Type != "emote" || fragment.Emote is null)
            {
                html.Append(fragment.Text);
                continue;
            }

            string? url = ResolveEmoteUrl(fragment.Emote);
            if (url is null)
            {
                html.Append(WebUtility.HtmlEncode(fragment.Text));
                continue;
            }

            html.Append("<img src=\"")
                .Append(url)
                .Append("\" alt=\"")
                .Append(WebUtility.HtmlEncode(fragment.Text))
                .Append("\" />");
        }

        return html.ToString();
    }

    // Replaces, in place, each standalone text fragment that is real HTML with its sanitised html fragment.
    private static void ConvertStandaloneHtmlFragments(ChatDecorationContext context)
    {
        for (int i = 0; i < context.Fragments.Count; i++)
        {
            ChatMessageFragment fragment = context.Fragments[i];
            if (fragment.Type != "text" || !IsRealHtml(fragment.Text))
                continue;

            context.Fragments[i] = new ChatMessageFragment
            {
                Type = "html",
                Text = ChatHtmlSanitizer.Sanitize(fragment.Text),
            };
        }
    }

    // Best-scale emote url: prefer 3x, then 2x, then 1x; null when the emote carries no url at all.
    private static string? ResolveEmoteUrl(ChatEmote emote)
    {
        if (emote.Urls.TryGetValue("3", out string? url3))
            return url3;
        if (emote.Urls.TryGetValue("2", out string? url2))
            return url2;
        if (emote.Urls.TryGetValue("1", out string? url1))
            return url1;
        return null;
    }

    // Real HTML (not stray angle-brackets in prose/maths): contains both brackets AND an actual opening or closing tag.
    private static bool IsRealHtml(string text) =>
        text.Contains('<')
        && text.Contains('>')
        && (HasOpeningHtmlTag(text) || HasClosingHtmlTag(text));

    // An opening tag is a '<' followed (past any leading '/' or '!') by a letter — distinguishes "<b" / "</b" from "1 < 2".
    private static bool HasOpeningHtmlTag(string text)
    {
        int index = text.IndexOf('<');
        if (index < 0)
            return false;

        for (int i = index + 1; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsLetter(c))
                return true;
            if (c == '/' || c == '!')
                continue;
            break;
        }

        return false;
    }

    // A closing tag is a "</" that is later terminated by a '>' with at least one character of tag name between them.
    private static bool HasClosingHtmlTag(string text)
    {
        int open = text.IndexOf("</", StringComparison.Ordinal);
        if (open < 0)
            return false;

        int close = text.IndexOf('>', open);
        return close > open + 2;
    }
}
