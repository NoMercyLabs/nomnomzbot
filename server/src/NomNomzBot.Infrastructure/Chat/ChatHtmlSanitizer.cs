// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Ganss.Xss;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// The single hardened sanitiser for the opt-in subscriber chat-HTML fragment (<c>HtmlFragmentAdapter</c>). Rendering
/// viewer-authored HTML in chat is powerful, so every such fragment passes through here first: a curated allow-list of
/// formatting, block, table, and media tags — plus <c>marquee</c> and <c>img</c> — while everything outside the list is
/// dropped. It keeps HtmlSanitizer's inherent defences (<c>&lt;script&gt;</c> and its subtree removed, every
/// <c>on*</c> event handler stripped, the <c>style</c> attribute and all inline CSS removed) and hardens the URL surface
/// to <b>https only</b>, so <c>javascript:</c>, <c>data:</c>, and plain-<c>http:</c> sources can never survive.
/// The underlying <see cref="HtmlSanitizer"/> is expensive to configure, so it is built once and reused — its
/// <see cref="HtmlSanitizer.Sanitize(string, string, AngleSharp.Html.IMarkupFormatter)"/> is thread-safe once configured.
/// </summary>
public static class ChatHtmlSanitizer
{
    // Formatting / inline, block, table, and the two "fun" media tags (marquee + img) the legacy bot rendered. Anything
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
        // audio + video playback (the sender's clips). Their url attributes (src/poster/srcset) are still
        // https-only via AllowedSchemes, and every on* handler is still stripped, so "sanitised" media = a plain
        // <video>/<audio> the browser plays, never a scripting surface.
        "audio",
        "video",
        "source",
        "track",
    ];

    // Attributes allowed on any tag. Deliberately excludes "style" (no inline CSS / url()), every "on*" handler, and
    // "id" (DOM-clobbering surface). "href"/"src"/"poster"/"srcset" carry URLs, guarded to https by AllowedSchemes.
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
        "title",
        "width",
        // <audio>/<video>/<source>/<track> attributes — DISPLAY ONLY. Deliberately excludes "autoplay", "loop",
        // "preload", and "muted": on an OBS browser-source there is no viewer to click, so autoplay/loop would let a
        // sender force disruptive media (loud audio, endless clips) onto the stream. The media renders as a player;
        // nothing plays until the streamer chooses to. "controls"/"poster"/"src" are the safe presentational surface.
        "controls",
        "default",
        "kind",
        "label",
        "playsinline",
        "poster",
        "srclang",
        "srcset",
        "type",
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

        // No inline CSS reaches the client: the style attribute is already off the allow-list; clearing the CSS
        // allow-lists too means an allowed element can never carry a url()/expression() payload.
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedAtRules.Clear();

        // https everywhere a URL can appear (img src, a href): strips plain http, and — by omission — javascript:,
        // data:, vbscript:, file:, and every other scheme.
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("https");

        // The scheme check only runs on attributes HtmlSanitizer treats as URLs. src/href are defaults, but the media
        // attributes we allow (poster, srcset) are NOT — register them so an http:/javascript: poster can't slip past.
        sanitizer.UriAttributes.Add("poster");
        sanitizer.UriAttributes.Add("srcset");

        // A disallowed element takes its whole subtree with it, so <script>alert(1)</script> leaves nothing behind
        // rather than an orphaned "alert(1)" text node.
        sanitizer.KeepChildNodes = false;

        return sanitizer;
    }
}
