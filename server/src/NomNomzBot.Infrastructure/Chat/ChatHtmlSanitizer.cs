// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using Ganss.Xss;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// The single hardened sanitiser for the opt-in subscriber chat-HTML fragment (<c>HtmlFragmentAdapter</c>). Rendering
/// viewer-authored HTML in chat is powerful, so every such fragment passes through here first: a curated allow-list of
/// formatting, block, and table tags — no video and no audio — plus <c>marquee</c> and <c>img</c> — while everything outside the list is
/// dropped. It keeps HtmlSanitizer's inherent defences (<c>&lt;script&gt;</c> and its subtree removed, every
/// <c>on*</c> event handler stripped), lets inline CSS through only for sizing, text and colour (capped by
/// <see cref="ChatHtmlSizeLimits"/>), and hardens the URL surface to <b>https only</b>, so <c>javascript:</c>,
/// <c>data:</c>, and plain-<c>http:</c> sources can never survive.
/// The underlying <see cref="HtmlSanitizer"/> is expensive to configure, so it is built once and reused — its
/// <see cref="HtmlSanitizer.Sanitize(string, string, AngleSharp.Html.IMarkupFormatter)"/> is thread-safe once configured.
/// </summary>
public static class ChatHtmlSanitizer
{
    // Formatting / inline, block, table, and the two "fun" tags (marquee + img) the legacy bot rendered. Anything
    // not listed here is removed by the sanitiser — the allow-list IS the security boundary, so it stays deliberately tight.
    private static readonly string[] AllowedTags =
    [
        // inline & text-level
        "a",
        "abbr",
        "b",
        "bdi",
        "bdo",
        "br",
        "cite",
        "code",
        "del",
        "dfn",
        "em",
        "i",
        "ins",
        "kbd",
        "mark",
        "q",
        "s",
        "samp",
        "small",
        "span",
        "strike",
        "strong",
        "sub",
        "sup",
        "time",
        "u",
        "var",
        "wbr",
        // block
        "blockquote",
        "dd",
        "div",
        "dl",
        "dt",
        "figcaption",
        "figure",
        "h1",
        "h2",
        "h3",
        "h4",
        "h5",
        "h6",
        "hr",
        "li",
        "ol",
        "p",
        "pre",
        "ul",
        // table
        "caption",
        "col",
        "colgroup",
        "table",
        "tbody",
        "td",
        "tfoot",
        "th",
        "thead",
        "tr",
        // media / fun
        "img",
        "marquee",
        // No video and no audio: audio, video, source and track are not allowed (owner, 2026-10-14).
    ];

    // Attributes allowed on any tag. Deliberately excludes every "on*" handler and "id" (DOM-clobbering surface).
    // "href"/"src"/"srcset" carry URLs, guarded to https by AllowedSchemes; "style" is held to
    // AllowedCssProperties.
    private static readonly string[] AllowedAttributes =
    [
        "alt",
        "class",
        "colspan",
        "dir",
        "height",
        "href",
        "lang",
        "rowspan",
        "src",
        "style",
        "title",
        "width",
        // img srcset. No media attributes: video and audio are not allowed.
        "srcset",
        // <marquee> presentational attributes, so the sender's scrolling text keeps its motion/colour (the effects
        // their current-bot messages actually used) — none of these carry script.
        "behavior",
        "bgcolor",
        "direction",
        "hspace",
        "scrollamount",
        "scrolldelay",
        "vspace",
    ];

    // Inline CSS a sender may use: sizing, text and colour. Nothing that positions, layers, transforms or animates an
    // element, and no property that takes a url().
    private static readonly HashSet<string> AllowedCssProperties =
    [
        "width",
        "height",
        "max-width",
        "max-height",
        "object-fit",
        "color",
        "background-color",
        "font-size",
        "font-style",
        "font-weight",
        "letter-spacing",
        "line-height",
        "text-align",
        "text-decoration",
        "text-shadow",
    ];

    private static readonly HtmlSanitizer Sanitizer = Build();

    /// <summary>Returns a sanitised copy of <paramref name="html"/> containing only the allow-listed, https-guarded subset.</summary>
    public static string Sanitize(string html) => Sanitizer.Sanitize(html);

    private static HtmlSanitizer Build()
    {
        HtmlSanitizer sanitizer = new();

        sanitizer.AllowedTags.Clear();
        foreach (string tag in AllowedTags)
            sanitizer.AllowedTags.Add(tag);

        sanitizer.AllowedAttributes.Clear();
        foreach (string attribute in AllowedAttributes)
            sanitizer.AllowedAttributes.Add(attribute);

        sanitizer.AllowedCssProperties.Clear();
        foreach (string property in AllowedCssProperties)
            sanitizer.AllowedCssProperties.Add(property);
        sanitizer.AllowedAtRules.Clear();

        // https everywhere a URL can appear (img src, a href): strips plain http, and — by omission — javascript:,
        // data:, vbscript:, file:, and every other scheme.
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("https");

        // The scheme check only runs on attributes HtmlSanitizer treats as URLs. src/href are defaults, but srcset
        // is NOT — register it so an http:/javascript: srcset can't slip past.
        sanitizer.UriAttributes.Add("srcset");

        // A disallowed element takes its whole subtree with it, so <script>alert(1)</script> leaves nothing behind
        // rather than an orphaned "alert(1)" text node.
        sanitizer.KeepChildNodes = false;

        // An img whose src is stripped (http:// or another disallowed scheme) must not survive as a blank image under
        // a caption: HtmlSanitizer removes only the attribute and keeps the src-less tag. Track every img that just
        // lost its src here, and drop the whole element once sanitisation of that node finishes. The one sanitiser
        // serves every channel's chat at once, so the record is thread-safe, and weak so a finished message's DOM is
        // not kept alive by it.
        ConditionalWeakTable<AngleSharp.Dom.IElement, object> elementsWithDroppedUrl = new();
        sanitizer.RemovingAttribute += (_, e) =>
        {
            if (e.Attribute.Name == "src" && e.Tag.TagName == "IMG")
                elementsWithDroppedUrl.AddOrUpdate(e.Tag, e.Tag);
        };
        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is not AngleSharp.Dom.IElement element)
                return;

            if (elementsWithDroppedUrl.TryGetValue(element, out object? _))
            {
                element.Remove();
                return;
            }

            LimitSizes(element);
        };

        return sanitizer;
    }

    private static void LimitSizes(AngleSharp.Dom.IElement element)
    {
        string? style = element.GetAttribute("style");
        if (style is not null)
        {
            string? limited = ChatHtmlSizeLimits.LimitStyle(style, AllowedCssProperties);
            if (limited is null)
                element.RemoveAttribute("style");
            else
                element.SetAttribute("style", limited);
        }

        foreach (string dimension in (string[])["width", "height"])
        {
            string? value = element.GetAttribute(dimension);
            if (value is null)
                continue;

            string? limited = ChatHtmlSizeLimits.LimitDimensionAttribute(dimension, value);
            if (limited is null)
                element.RemoveAttribute(dimension);
            else
                element.SetAttribute(dimension, limited);
        }
    }
}
