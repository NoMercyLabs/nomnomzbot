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

    public TemplateHelperEntry ToEntry() =>
        new(
            Key,
            [.. Enum.GetValues<TemplateHelperContext>()],
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
