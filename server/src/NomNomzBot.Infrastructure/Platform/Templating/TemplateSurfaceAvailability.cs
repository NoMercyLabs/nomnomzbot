// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using System.Text;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating;

/// <summary>One cell of the helper-by-surface matrix: available, or absent with a code and a reason.</summary>
/// <param name="Available">True when the registry declares the helper for the surface.</param>
/// <param name="Code"><c>Y</c> when available, otherwise the code of the rule that explains the absence.</param>
/// <param name="Reason">Empty when available, otherwise a plain-English fragment that starts with "it".</param>
public sealed record TemplateSurfaceCell(bool Available, string Code, string Reason);

/// <summary>
/// The one rule table behind "where does a helper work, and why not here". The save-time validator names the
/// reason in its error and <see cref="RenderMarkdown"/> prints the same reasons in <c>docs/templates/surfaces.md</c>,
/// so the message and the page cannot drift apart. Availability itself is the registry's: a helper is available
/// on a surface exactly when its row lists the surface. The rules only explain an absence.
/// </summary>
public static class TemplateSurfaceAvailability
{
    private const string AvailableCode = "Y";

    /// <param name="Code">The short code printed in a matrix cell.</param>
    /// <param name="KeyPrefixes">The rule applies to a key that starts with one of these; empty means any key.</param>
    /// <param name="Surfaces">The surfaces the rule explains; empty means any surface.</param>
    /// <param name="Reason">A fragment that starts with "it" and carries no final full stop.</param>
    private sealed record Rule(
        string Code,
        string[] KeyPrefixes,
        TemplateHelperContext[] Surfaces,
        string Reason
    );

    /// <summary>The first rule that matches an absent cell wins, so the specific rules come before the general ones.</summary>
    private static readonly Rule[] Rules =
    [
        new(
            "A",
            ["args"],
            [
                TemplateHelperContext.EventResponse,
                TemplateHelperContext.Timer,
                TemplateHelperContext.Discord,
                TemplateHelperContext.Webhook,
                TemplateHelperContext.Viewer,
            ],
            "it needs a chat command, and only commands and pipelines carry one"
        ),
        new(
            "U",
            ["user.", "target.", "chat."],
            [TemplateHelperContext.Timer, TemplateHelperContext.Discord],
            "it needs a trigger that carries a viewer or a chat message, and timers and Discord notifications have none"
        ),
        new(
            "V",
            [],
            [TemplateHelperContext.Viewer],
            "viewer text allows only the helpers whose registry row lists it, and this row does not"
        ),
        new(
            "N",
            [],
            [],
            "the registry row of this helper does not list this surface, and no general rule explains it"
        ),
    ];

    private static readonly Dictionary<TemplateHelperContext, string> SurfaceNames = new()
    {
        [TemplateHelperContext.Command] = "commands",
        [TemplateHelperContext.EventResponse] = "event responses",
        [TemplateHelperContext.Timer] = "timers",
        [TemplateHelperContext.Pipeline] = "pipelines",
        [TemplateHelperContext.Discord] = "Discord notifications",
        [TemplateHelperContext.Webhook] = "webhooks",
        [TemplateHelperContext.Viewer] = "viewer text",
    };

    private static readonly TemplateHelperContext[] AllSurfaces =
    [
        .. Enum.GetValues<TemplateHelperContext>(),
    ];

    /// <summary>The plural name of a surface as it reads in a sentence, such as "event responses".</summary>
    public static string SurfaceName(TemplateHelperContext surface) => SurfaceNames[surface];

    /// <summary>The cell for one helper on one surface.</summary>
    public static TemplateSurfaceCell Describe(
        TemplateHelperEntry entry,
        TemplateHelperContext surface
    )
    {
        if (entry.Contexts.Contains(surface))
            return new TemplateSurfaceCell(true, AvailableCode, string.Empty);

        Rule rule = Rules.First(r => Applies(r, entry.Key, surface));
        return new TemplateSurfaceCell(false, rule.Code, rule.Reason);
    }

    /// <summary>
    /// The validator sentence for a key the registry knows on other surfaces only: the surface it was used on,
    /// the reason, and where it does work.
    /// </summary>
    public static string DescribeMissing(
        string key,
        TemplateHelperContext surface,
        IReadOnlyList<TemplateHelperEntry> declaredEntries
    )
    {
        TemplateSurfaceCell cell = Describe(declaredEntries[0], surface);
        string works = string.Join(
            ", ",
            AllSurfaces
                .Where(s => declaredEntries.Any(e => e.Contexts.Contains(s)))
                .Select(SurfaceName)
        );
        return $"'{{{key}}}' is not available in {SurfaceName(surface)} because {cell.Reason}. It works in: {works}";
    }

    /// <summary>The whole matrix document, generated from the registry: the same text on every run for the same rows.</summary>
    public static string RenderMarkdown(ITemplateHelperRegistry registry)
    {
        StringBuilder text = new();
        text.Append("# Template helpers by surface\n\n");
        text.Append(
            "This page is generated from the helper registry by `TemplateSurfaceAvailability.RenderMarkdown`. "
                + "Do not edit it by hand: a test fails when it differs from the registry. "
                + "`Y` means the helper works on that surface. A code means it does not, and the legend at the end gives the reason.\n\n"
        );

        IEnumerable<IGrouping<string, TemplateHelperEntry>> groups = registry
            .All.GroupBy(entry => GroupOf(entry.Key))
            .OrderBy(group => group.Key == "" ? 0 : 1)
            .ThenBy(group => group.Key, StringComparer.Ordinal);
        foreach (IGrouping<string, TemplateHelperEntry> group in groups)
        {
            text.Append(group.Key == "" ? "## Keys without a dot\n\n" : $"## {group.Key}\n\n");
            text.Append("| Helper |");
            foreach (TemplateHelperContext surface in AllSurfaces)
                text.Append($" {Capitalize(SurfaceName(surface))} |");
            text.Append('\n');
            text.Append("| --- |");
            text.Append(string.Concat(AllSurfaces.Select(_ => " --- |")));
            text.Append('\n');
            foreach (TemplateHelperEntry entry in group.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                text.Append($"| `{entry.Key.Replace("|", "\\|")}` |");
                foreach (TemplateHelperContext surface in AllSurfaces)
                    text.Append($" {Describe(entry, surface).Code} |");
                text.Append('\n');
            }
            text.Append('\n');
        }

        text.Append("## Legend\n\n| Code | Why the helper is absent |\n| --- | --- |\n");
        foreach (Rule rule in Rules)
            text.Append($"| `{rule.Code}` | {rule.Reason} |\n");
        return text.ToString();
    }

    private static bool Applies(Rule rule, string key, TemplateHelperContext surface) =>
        (rule.Surfaces.Length == 0 || rule.Surfaces.Contains(surface))
        && (
            rule.KeyPrefixes.Length == 0
            || rule.KeyPrefixes.Any(p => key.StartsWith(p, StringComparison.OrdinalIgnoreCase))
        );

    private static string GroupOf(string key)
    {
        int dot = key.IndexOf('.');
        return dot < 0 ? string.Empty : key[..dot];
    }

    private static string Capitalize(string name) => char.ToUpperInvariant(name[0]) + name[1..];
}
