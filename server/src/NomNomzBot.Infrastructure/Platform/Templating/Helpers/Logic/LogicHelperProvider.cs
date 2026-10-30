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
    /// as-built helpers already own a string for). Every logic helper writes nothing and reads only its own
    /// arguments, so a row is open to a viewer's own text unless <paramref name="viewer"/> is false.
    /// </summary>
    protected static TemplateHelperEntry Row(
        string key,
        string sample,
        bool lazy = false,
        string? prefix = null,
        string? descriptionKey = null,
        bool viewer = true
    ) =>
        new(
            key,
            viewer ? TemplateHelperContexts.StandardAndViewer : TemplateHelperContexts.Standard,
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
