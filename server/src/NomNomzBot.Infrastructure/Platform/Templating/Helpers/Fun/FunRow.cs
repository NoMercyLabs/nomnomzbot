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
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Counters;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Fun;

/// <summary>What a fun helper sees: its rendered arguments, the render scope, the injectable random source and the channel's lists.</summary>
internal readonly record struct FunCall(
    IReadOnlyList<string> Args,
    TemplateRenderScope Scope,
    Random Random,
    ListSource Lists,
    CancellationToken Ct
);

/// <summary>
/// One <c>fun.*</c> helper form: its registry row and how it renders. A null result prints the token as
/// written, which is what a bad argument gets.
/// </summary>
internal sealed record FunRow(
    string Key,
    string Sample,
    Func<FunCall, ValueTask<string?>> Render,
    TemplateHelperFreshness Freshness,
    TemplateHelperScope Scope
)
{
    /// <summary>A form that draws or computes from its own arguments and the clock only, so it works with no channel.</summary>
    public static FunRow Pure(
        string key,
        string sample,
        Func<FunCall, string?> render,
        TemplateHelperScope scope = TemplateHelperScope.Global
    ) =>
        new(
            key,
            sample,
            call => ValueTask.FromResult(render(call)),
            TemplateHelperFreshness.Pure,
            scope
        );

    /// <summary>A form that reads the channel's data (its pick lists).</summary>
    public static FunRow Channel(
        string key,
        string sample,
        Func<FunCall, ValueTask<string?>> render
    ) => new(key, sample, render, TemplateHelperFreshness.Live, TemplateHelperScope.Channel);

    /// <summary>
    /// A row that draws or computes from its own arguments and the clock only (<see cref="Pure"/>) writes nothing and
    /// reads nothing private, so it is open to a viewer's own text. A row that reads the channel's pick lists
    /// (<see cref="Channel"/>) stays closed to it.
    /// </summary>
    public TemplateHelperEntry ToEntry() =>
        new(
            Key,
            Freshness == TemplateHelperFreshness.Pure
                ? TemplateHelperContexts.StandardAndViewer
                : TemplateHelperContexts.Standard,
            new LocalizedText("template.helper." + Key)
        )
        {
            Sample = Sample,
            Freshness = Freshness,
            Scope = Scope,
        };
}

/// <summary>One responsibility inside the <c>fun</c> namespace (dice, coin, games, ratings, ...). A group has a parameterless constructor and the provider finds it by reflection.</summary>
internal interface IFunHelperGroup
{
    IReadOnlyList<FunRow> Rows { get; }
}

internal abstract class FunHelperGroup(params FunRow[] rows) : IFunHelperGroup
{
    public IReadOnlyList<FunRow> Rows { get; } = rows;
}
