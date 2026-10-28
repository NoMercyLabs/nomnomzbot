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
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Logic;

/// <summary>
/// The shared half of the logic providers (catalog section 7.5): every logic helper is pure, so there is nothing
/// to prepare, and every row is built the same way by <see cref="Row"/>.
/// </summary>
public abstract class LogicHelperProvider : ITemplateHelperProvider
{
    public abstract string Namespace { get; }

    public abstract IReadOnlyList<TemplateHelperEntry> Entries { get; }

    public ValueTask PrepareAsync(
        TemplateRenderScope scope,
        IReadOnlyCollection<HelperNode> nodes,
        CancellationToken cancellationToken
    ) => ValueTask.CompletedTask;

    public abstract ValueTask<string?> RenderAsync(
        TemplateRenderScope scope,
        HelperNode node,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// One registry row. The help string key is <c>template.helper.&lt;key&gt;</c> unless
    /// <paramref name="descriptionKey"/> names another one (a key that holds <c>&lt;path&gt;</c>, or one the
    /// as-built helpers already own a string for).
    /// </summary>
    protected static TemplateHelperEntry Row(
        string key,
        string sample,
        bool lazy = false,
        string? prefix = null,
        string? descriptionKey = null
    ) =>
        new(
            key,
            TemplateHelperContexts.Standard,
            new LocalizedText(descriptionKey ?? "template.helper." + key),
            prefix
        )
        {
            Sample = sample,
            LazyArguments = lazy,
            Freshness = TemplateHelperFreshness.Pure,
            Scope = TemplateHelperScope.Global,
        };

    protected static ValueTask<string?> Result(string? text) => ValueTask.FromResult(text);
}
