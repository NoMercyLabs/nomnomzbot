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

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Arguments;

/// <summary>One node of a render: the token and the words of the command it runs in.</summary>
internal sealed record ArgumentCall(HelperNode Node, CommandWords Words)
{
    public IReadOnlyList<string> Arguments => Node.Arguments;
}

/// <summary>
/// One <c>args.*</c> helper. A fixed row (<c>args.rest</c>) has an exact <see cref="Key"/>. A shaped row
/// (<c>args.&lt;n&gt;.upper</c>) has a display <see cref="Key"/> and a <see cref="Shape"/> that says which keys
/// it takes; it claims the prefix <c>args.</c> in the registry. <see cref="Render"/> returns null for a bad
/// argument, so the token prints as written.
/// </summary>
internal sealed record ArgumentRow(
    string Key,
    string Sample,
    string HelpId,
    Func<ArgumentCall, string?> Render,
    Func<string, bool>? Shape = null
)
{
    private const string Prefix = "args.";

    /// <summary>A row with one exact key; its help string is <c>template.helper.&lt;key&gt;</c>.</summary>
    public static ArgumentRow Exact(
        string key,
        string sample,
        Func<ArgumentCall, string?> render
    ) => new(key, sample, key, render);

    /// <summary>A row for every key of a shape; <paramref name="helpId"/> names its help string.</summary>
    public static ArgumentRow Shaped(
        string key,
        string helpId,
        string sample,
        Func<string, bool> shape,
        Func<ArgumentCall, string?> render
    ) => new(key, sample, helpId, render, shape);

    public bool Accepts(string key) =>
        Shape is null ? string.Equals(key, Key, StringComparison.OrdinalIgnoreCase) : Shape(key);

    public TemplateHelperEntry ToEntry() =>
        new(
            Key,
            [TemplateHelperContext.Command, TemplateHelperContext.Pipeline],
            new LocalizedText("template.helper." + HelpId),
            Shape is null ? null : Prefix
        )
        {
            Sample = Sample,
            Freshness = TemplateHelperFreshness.Seed,
            Scope = TemplateHelperScope.Global,
        };
}
