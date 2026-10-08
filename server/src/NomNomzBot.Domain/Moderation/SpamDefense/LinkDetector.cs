// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace NomNomzBot.Domain.Moderation.SpamDefense;

/// <summary>A link found in a message: the text as matched, and its lower-case host without port or user info.</summary>
public sealed record DetectedLink(string Text, string Host);

/// <summary>
/// Finds links in chat, written with a scheme (<c>https://x.y/z</c>) or without one (<c>example.com/x</c>,
/// <c>sub.example.co.uk</c>, <c>www.example.com</c>).
///
/// <para>The text is read through <see cref="MessageNormalizer.ReadableForm"/> first, so a fullwidth
/// <c>ｅｘａｍｐｌｅ．ｃｏｍ</c>, a Cyrillic letter or a zero-width space inside the name cannot hide a link.</para>
///
/// <para><b>Why a known-TLD list and not "anything.with.a.dot".</b> A dot between two words is ordinary
/// typing: <c>end.of</c>, <c>v1.2</c>, <c>3.5</c>, <c>e.g.</c>. A bare domain is only a link when its last
/// label is a real top-level domain, so those pass and <c>example.com</c> does not. The one cost is the
/// handful of real TLDs that are also short English words (<c>is</c>, <c>it</c>, <c>so</c>, <c>no</c> ...):
/// <c>ok.so</c> is a typo far more often than a site, so for those a bare two-label form is only a link
/// when it carries a path or port, or starts with <c>www.</c>.</para>
/// </summary>
public static class LinkDetector
{
    /// <summary>Generic and commonly abused TLDs. Deliberately not the full registry: words such as "world" or "best" are TLDs and ordinary English.</summary>
    private static readonly string[] GenericTlds =
    [
        "com",
        "net",
        "org",
        "edu",
        "gov",
        "mil",
        "int",
        "info",
        "biz",
        "pro",
        "mobi",
        "name",
        "xyz",
        "top",
        "site",
        "online",
        "shop",
        "store",
        "live",
        "app",
        "dev",
        "page",
        "link",
        "click",
        "club",
        "fun",
        "icu",
        "vip",
        "work",
        "tech",
        "cloud",
        "ai",
        "bot",
        "chat",
        "stream",
        "games",
        "game",
        "win",
        "bet",
        "casino",
        "porn",
        "xxx",
        "sex",
        "blog",
    ];

    /// <summary>ISO 3166 country-code TLDs.</summary>
    private static readonly string[] CountryTlds =
    [
        "ac",
        "ad",
        "ae",
        "af",
        "ag",
        "al",
        "am",
        "ao",
        "aq",
        "ar",
        "as",
        "at",
        "au",
        "aw",
        "ax",
        "az",
        "ba",
        "bb",
        "bd",
        "be",
        "bf",
        "bg",
        "bh",
        "bi",
        "bj",
        "bm",
        "bn",
        "bo",
        "br",
        "bs",
        "bt",
        "bw",
        "by",
        "bz",
        "ca",
        "cc",
        "cd",
        "cf",
        "cg",
        "ch",
        "ci",
        "ck",
        "cl",
        "cm",
        "cn",
        "co",
        "cr",
        "cu",
        "cv",
        "cw",
        "cx",
        "cy",
        "cz",
        "de",
        "dj",
        "dk",
        "dm",
        "do",
        "dz",
        "ec",
        "ee",
        "eg",
        "es",
        "et",
        "eu",
        "fi",
        "fj",
        "fk",
        "fm",
        "fo",
        "fr",
        "ga",
        "gd",
        "ge",
        "gf",
        "gg",
        "gh",
        "gi",
        "gl",
        "gm",
        "gn",
        "gp",
        "gq",
        "gr",
        "gs",
        "gt",
        "gu",
        "gw",
        "gy",
        "hk",
        "hm",
        "hn",
        "hr",
        "ht",
        "hu",
        "id",
        "ie",
        "il",
        "im",
        "in",
        "io",
        "iq",
        "ir",
        "is",
        "it",
        "je",
        "jm",
        "jo",
        "jp",
        "ke",
        "kg",
        "kh",
        "ki",
        "km",
        "kn",
        "kp",
        "kr",
        "kw",
        "ky",
        "kz",
        "la",
        "lb",
        "lc",
        "li",
        "lk",
        "lr",
        "ls",
        "lt",
        "lu",
        "lv",
        "ly",
        "ma",
        "mc",
        "md",
        "me",
        "mg",
        "mh",
        "mk",
        "ml",
        "mm",
        "mn",
        "mo",
        "mp",
        "mq",
        "mr",
        "ms",
        "mt",
        "mu",
        "mv",
        "mw",
        "mx",
        "my",
        "mz",
        "na",
        "nc",
        "ne",
        "nf",
        "ng",
        "ni",
        "nl",
        "no",
        "np",
        "nr",
        "nu",
        "nz",
        "om",
        "pa",
        "pe",
        "pf",
        "pg",
        "ph",
        "pk",
        "pl",
        "pm",
        "pn",
        "pr",
        "ps",
        "pt",
        "pw",
        "py",
        "qa",
        "re",
        "ro",
        "rs",
        "ru",
        "rw",
        "sa",
        "sb",
        "sc",
        "sd",
        "se",
        "sg",
        "sh",
        "si",
        "sk",
        "sl",
        "sm",
        "sn",
        "so",
        "sr",
        "ss",
        "st",
        "su",
        "sv",
        "sx",
        "sy",
        "sz",
        "tc",
        "td",
        "tf",
        "tg",
        "th",
        "tj",
        "tk",
        "tl",
        "tm",
        "tn",
        "to",
        "tr",
        "tt",
        "tv",
        "tw",
        "tz",
        "ua",
        "ug",
        "uk",
        "us",
        "uy",
        "uz",
        "va",
        "vc",
        "ve",
        "vg",
        "vi",
        "vn",
        "vu",
        "wf",
        "ws",
        "ye",
        "yt",
        "za",
        "zm",
        "zw",
    ];

    /// <summary>TLDs that are also everyday two-letter words; see the type remarks.</summary>
    private static readonly HashSet<string> AmbiguousTlds =
    [
        "am",
        "as",
        "at",
        "be",
        "by",
        "do",
        "id",
        "in",
        "is",
        "it",
        "la",
        "ma",
        "me",
        "my",
        "no",
        "pa",
        "re",
        "so",
        "to",
        "us",
    ];

    private static readonly Regex LinkPattern = BuildPattern();

    /// <summary>Every link in <paramref name="text"/>, in order. Never null.</summary>
    public static IReadOnlyList<DetectedLink> Find(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        string readable = MessageNormalizer.ReadableForm(text);
        List<DetectedLink> links = [];
        foreach (Match match in LinkPattern.Matches(readable))
        {
            DetectedLink? link = match.Groups["scheme"].Success
                ? FromSchemeUrl(match.Value)
                : FromBareDomain(match);
            if (link is not null)
                links.Add(link);
        }

        return links;
    }

    private static DetectedLink FromSchemeUrl(string url)
    {
        string afterScheme = url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..];
        int end = afterScheme.IndexOfAny(['/', '?', '#']);
        string authority = end < 0 ? afterScheme : afterScheme[..end];

        // `http://good.com@evil.com/` is evil.com: everything before the last '@' is user info.
        authority = authority[(authority.LastIndexOf('@') + 1)..];
        int colon = authority.LastIndexOf(':');
        if (colon >= 0 && authority[(colon + 1)..].All(char.IsAsciiDigit))
            authority = authority[..colon];

        return new DetectedLink(url, authority.TrimEnd('.'));
    }

    private static DetectedLink? FromBareDomain(Match match)
    {
        string host = match.Groups["host"].Value;
        bool hasTail = match.Groups["tail"].Length > 0;
        string[] labels = host.Split('.');
        bool ambiguous = AmbiguousTlds.Contains(labels[^1]);

        if (ambiguous && labels.Length == 2 && !hasTail)
            return null;

        return new DetectedLink(match.Value, host);
    }

    private static Regex BuildPattern()
    {
        string tlds = string.Join('|', GenericTlds.Concat(CountryTlds).Distinct());
        string label = @"[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?";

        // Not preceded by a word character, '@' (an e-mail address is not a link), '.', '/' or '-', so the
        // match starts at the real beginning of a token.
        return new Regex(
            @"(?<![a-z0-9@._/-])(?:(?<scheme>https?://)\S+|"
                + $@"(?<host>(?:{label}\.)+(?:{tlds}|xn--[a-z0-9-]+))"
                + @"(?![a-z0-9@-])(?<tail>(?::\d{1,5})?(?:[/?#]\S*)?))",
            RegexOptions.Compiled | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(250)
        );
    }
}
