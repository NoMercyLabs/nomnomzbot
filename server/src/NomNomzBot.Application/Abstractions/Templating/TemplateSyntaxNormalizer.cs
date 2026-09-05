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

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>
/// Rewrites the StreamElements-style <c>${variable}</c> form to this product's only supported
/// placeholder syntax, <c>{variable}</c>.
/// <para>
/// Why this exists: the resolver's token pattern matches <c>{...}</c> and substitutes it, leaving any
/// preceding character alone — so a template carried over as <c>${user}</c> rendered as
/// <c>$Astro</c>, a stray dollar in front of every name. Reported from a live stream on the
/// <c>!lurk</c> response.
/// </para>
/// <para>
/// The engine is deliberately NOT changed to swallow a leading <c>$</c>. This bot has an economy, and
/// <c>You have ${points}</c> may well mean a literal dollar sign followed by an amount; teaching the
/// resolver to eat the <c>$</c> would silently destroy that. Owner's call, verbatim: "templates should
/// not use ${} but just {}".
/// </para>
/// <para>
/// So the rewrite is deliberately narrow: the <c>$</c> is dropped ONLY when the braces hold a name the
/// product actually knows as a template variable, enumerated from
/// <see cref="TemplateHelperRegistry.All"/> rather than any hand-written list. <c>${user}</c> becomes
/// <c>{user}</c>; <c>${points}</c>, where <c>points</c> is not a variable, keeps its dollar sign
/// untouched.
/// </para>
/// </summary>
public static class TemplateSyntaxNormalizer
{
    // Deliberately matches the resolver's own VariablePattern body ([^{}]+) so the two agree on what a
    // placeholder is; anything the resolver would not treat as one is not rewritten here either.
    private static readonly Regex DollarPlaceholder = new(
        @"\$\{([^{}]+)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    /// <summary>
    /// Returns <paramref name="template"/> with every <c>${known.variable}</c> rewritten to
    /// <c>{known.variable}</c>. Null, empty, and templates with no <c>${</c> at all are returned
    /// unchanged (reference-equal), so this is safe to call on every write.
    /// </summary>
    public static string? Normalize(string? template)
    {
        if (string.IsNullOrEmpty(template) || !template.Contains("${", StringComparison.Ordinal))
            return template;

        return DollarPlaceholder.Replace(
            template,
            static match =>
                IsKnownPlaceholder(match.Groups[1].Value) ? match.Value[1..] : match.Value
        );
    }

    /// <summary>
    /// True when the braces hold something the resolver can actually substitute: a registered helper,
    /// or one of the two structured forms the resolver handles by prefix rather than by registry entry
    /// (<c>custom.&lt;scope&gt;.&lt;key&gt;</c> and <c>transform.&lt;name&gt;:&lt;value&gt;</c>).
    /// </summary>
    private static bool IsKnownPlaceholder(string placeholderKey)
    {
        string key = placeholderKey.Trim();
        if (key.Length == 0)
            return false;

        if (
            key.StartsWith("custom.", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("transform.", StringComparison.OrdinalIgnoreCase)
        )
            return true;

        foreach (TemplateHelperEntry entry in TemplateHelperRegistry.All)
        {
            if (entry.Matches(key))
                return true;
        }

        return false;
    }
}
