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
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Composing;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Variables;

/// <summary>
/// <c>{set:name=value}</c> and <c>{var.name}</c> (catalog section 7.5). A set keeps the value in the render's
/// <see cref="TemplateRenderScope.SetVariables"/> and prints nothing; the resolver also reads a plain
/// <c>{name}</c> from that bag when no registered key has the name. A name is letters, digits and underscores
/// and starts with a letter or underscore, so it can never hold a dot and never clashes with a dotted key. A name
/// that a seed variable already holds is refused (the seed wins). A value lives only in its own render.
/// </summary>
public sealed partial class VariableTemplateHelpers : ITemplateHelperProvider
{
    private const string ReadPrefix = "var.";

    public string Namespace => "var";

    public IReadOnlyList<TemplateHelperEntry> Entries { get; } =
    [
        ComposingRows.Row("set", string.Empty, TemplateHelperFreshness.Pure),
        ComposingRows.Row(
            "var.<name>",
            "5",
            TemplateHelperFreshness.Pure,
            prefix: ReadPrefix,
            descriptionKey: "template.helper.var.name"
        ),
    ];

    public ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    ) => ValueTask.CompletedTask;

    public ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    ) =>
        ValueTask.FromResult(
            node.Key.StartsWith(ReadPrefix, StringComparison.OrdinalIgnoreCase)
                ? Read(scope, node)
                : Set(scope, node)
        );

    private static string? Read(TemplateRenderScope scope, HelperNode node) =>
        node.Arguments.Count == 0
        && scope.SetVariables.TryGetValue(node.Key[ReadPrefix.Length..], out string? value)
            ? value
            : null;

    /// <summary>The value may hold <c>|</c>, which the argument split cut apart: the pieces are joined back.</summary>
    private static string? Set(TemplateRenderScope scope, HelperNode node)
    {
        string text = string.Join("|", node.Arguments);
        int equals = text.IndexOf('=', StringComparison.Ordinal);
        if (equals < 1)
            return null;

        string name = text[..equals].Trim();
        if (!ValidName().IsMatch(name) || scope.Variables.ContainsKey(name))
            return null;

        scope.SetVariables[name] = text[(equals + 1)..].Trim();
        return string.Empty;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ValidName();
}
