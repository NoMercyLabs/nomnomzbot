// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Bot;

internal readonly record struct BotCall(BotData Data, TemplateRenderScope Scope)
{
    public ChannelContext? Channel => Scope.Channel;

    public DateTimeOffset Now => Scope.Now;

    public CultureInfo Culture => NumberCulture.For(Scope.Language);

    /// <summary>The seed variable of the render, or an empty text.</summary>
    public string Seed(string key) =>
        Scope.Variables.TryGetValue(key, out string? value) ? value : string.Empty;
}

/// <summary>
/// One <c>bot.*</c> helper form. A form takes no argument. A null result prints the token as written; an empty
/// text is what a value that is not known (or not set) prints.
/// </summary>
internal sealed record BotRow(
    string Key,
    string Sample,
    BotSources Sources,
    TemplateHelperFreshness Freshness,
    Func<BotCall, Task<string?>> Render
)
{
    /// <summary>A form that reads the process, the fixed texts or the channel in memory; no fetch.</summary>
    public static BotRow Memory(string key, string sample, Func<BotCall, string?> print) =>
        new(
            key,
            sample,
            BotSources.None,
            TemplateHelperFreshness.Memory,
            call => Task.FromResult(print(call))
        );

    /// <summary>A form that prints one fixed text.</summary>
    public static BotRow Fixed(string key, string sample, string text) =>
        new(
            key,
            sample,
            BotSources.None,
            TemplateHelperFreshness.Pure,
            _ => Task.FromResult<string?>(text)
        );

    /// <summary>A form that reads one service source (once per render).</summary>
    public static BotRow Read(
        string key,
        string sample,
        BotSources sources,
        Func<BotCall, Task<string?>> render
    ) => new(key, sample, sources, TemplateHelperFreshness.Cached, render);

    public Task<string?> RenderAsync(BotCall call, IReadOnlyList<string> arguments) =>
        arguments.Count > 0 ? Task.FromResult<string?>(null) : Render(call);

    public TemplateHelperEntry ToEntry() =>
        new(
            Key,
            [.. Enum.GetValues<TemplateHelperContext>()],
            new LocalizedText("template.helper." + Key)
        )
        {
            Sample = Sample,
            Source = Sources == BotSources.None ? null : Sources.ToString().ToLowerInvariant(),
            Freshness = Freshness,
        };
}

internal interface IBotHelperGroup
{
    IReadOnlyList<BotRow> Rows { get; }
}

internal abstract class BotHelperGroup(params BotRow[] rows) : IBotHelperGroup
{
    public IReadOnlyList<BotRow> Rows { get; } = rows;
}
