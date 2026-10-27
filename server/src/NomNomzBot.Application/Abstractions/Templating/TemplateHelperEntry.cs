// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Localization;

namespace NomNomzBot.Application.Abstractions.Templating;

/// <summary>
/// One entry in the <see cref="TemplateHelperRegistry"/> — a single resolvable helper key (e.g.
/// <c>user.name</c>) or a parameterized prefix family (e.g. <c>args.&lt;n&gt;</c>, matched by
/// <see cref="Prefix"/>). <see cref="TemplateResolver"/> and this registry are kept in sync by
/// <c>TemplateHelperCoverageTests</c> — a helper resolvable there but unregistered here (or vice
/// versa) fails that guard.
/// A provider handles a token only when one of its entries holds the key (an exact key, an alias, or a
/// prefix family); the key's namespace alone never gives a provider the token.
/// </summary>
/// <param name="Key">
/// The canonical placeholder text as written in a template (without braces). For a prefix family this
/// is the display form, e.g. <c>args.&lt;n&gt;</c> — use <see cref="Prefix"/> for matching.
/// </param>
/// <param name="Contexts">The template surfaces this helper is valid in.</param>
/// <param name="Description">Localized, user-facing description key — never an English literal.</param>
/// <param name="Prefix">
/// Null for a literal key (matched case-insensitively against the full placeholder). Non-null for a
/// parameterized family — the placeholder is valid when it starts with this prefix and has at least one
/// character after it (e.g. <c>Prefix="args."</c> matches <c>args.1</c>, <c>args.2</c>, ...).
/// </param>
/// <param name="EventScoped">
/// True when this helper is only seeded by SOME EventSub triggers, not every one that reaches
/// <see cref="TemplateHelperContext.EventResponse"/> — e.g. <c>tier</c> only fires from a subscription
/// event, never a raid (S-OWN16). <c>TemplateHelperRegistry.ForContext(context, eventType)</c> checks
/// such an entry against <c>EventResponsePresetCatalog</c>'s per-event variable list; false (the
/// default) means the helper is valid for every event that context accepts.
/// </param>
public sealed record TemplateHelperEntry(
    string Key,
    IReadOnlyList<TemplateHelperContext> Contexts,
    LocalizedText Description,
    string? Prefix = null,
    bool EventScoped = false
)
{
    /// <summary>A plain, realistic example value for this helper, shown in the variable picker.</summary>
    public string Sample { get; init; } = string.Empty;

    /// <summary>Older keys that resolve to this entry (catalog section 2, rule 5). An alias is never removed.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>
    /// True when the helper chooses among its arguments (<c>if</c>, <c>switch</c>, <c>default</c>, <c>chance</c>).
    /// The renderer then passes the raw argument text in <see cref="HelperNode.Arguments"/> and renders none
    /// of it first; the provider renders the ones it picks with <see cref="HelperNode.RenderArgumentAsync"/>,
    /// so a branch that is not chosen never resolves. False (the default) renders every argument first.
    /// </summary>
    public bool LazyArguments { get; init; }

    /// <summary>The provider's fetch group: entries with the same source share one fetch per render. Null: no fetch.</summary>
    public string? Source { get; init; }

    /// <summary>How fresh the value is. The as-built rows keep the default: they fetch on each render.</summary>
    public TemplateHelperFreshness Freshness { get; init; } = TemplateHelperFreshness.Live;

    /// <summary>Whose data the helper reads.</summary>
    public TemplateHelperScope Scope { get; init; } = TemplateHelperScope.Channel;

    /// <summary>True when <paramref name="placeholderKey"/> (already trimmed, no braces) matches this entry or one of its aliases.</summary>
    public bool Matches(string placeholderKey)
    {
        if (Aliases.Contains(placeholderKey, StringComparer.OrdinalIgnoreCase))
            return true;

        if (Prefix is not null)
            return placeholderKey.Length > Prefix.Length
                && placeholderKey.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

        return string.Equals(Key, placeholderKey, StringComparison.OrdinalIgnoreCase);
    }
}
