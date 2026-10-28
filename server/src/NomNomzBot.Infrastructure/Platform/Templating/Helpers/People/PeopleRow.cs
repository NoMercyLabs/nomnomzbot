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

using System.Globalization;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>What a row gets to render one token: the person's data, the arguments and the render's clock.</summary>
internal sealed record PeopleCall(
    ViewerData Data,
    IReadOnlyList<string> Args,
    string KeyTail,
    TemplateRenderScope Scope,
    CancellationToken Ct
)
{
    public ViewerRef Who => Data.Who;

    public DateTimeOffset Now => Scope.Now;

    public CultureInfo Culture => NumberCulture.For(Scope.Language);
}

/// <summary>
/// One helper form of the <c>user.*</c> and <c>target.*</c> families: the key after the namespace, the sides it
/// exists for and how it renders. A prefix row (<c>data.</c>) takes the rest of the key as <c>KeyTail</c>.
/// </summary>
internal sealed record PeopleRow(
    string Suffix,
    ViewerSides Sides,
    string Sample,
    Func<PeopleCall, Task<string?>> Render
)
{
    /// <summary>The sample of the <c>target.*</c> side when it differs from the <c>user.*</c> sample.</summary>
    public string? TargetSample { get; init; }

    /// <summary>The fetch group of the row; rows with the same source share one read per render.</summary>
    public string? Source { get; init; }

    public TemplateHelperFreshness Freshness { get; init; } = TemplateHelperFreshness.Live;

    /// <summary>True when the row owns every key that starts with <c>Suffix</c>.</summary>
    public bool IsPrefix { get; init; }

    /// <summary>A row that renders from its own text only (a synchronous answer).</summary>
    public static PeopleRow Text(
        string suffix,
        ViewerSides sides,
        string sample,
        Func<PeopleCall, string?> render
    ) =>
        new(suffix, sides, sample, call => Task.FromResult(render(call)))
        {
            Freshness = TemplateHelperFreshness.Seed,
        };

    /// <summary>A row that reads a service (the source names the read it shares with its group).</summary>
    public static PeopleRow Live(
        string suffix,
        ViewerSides sides,
        string sample,
        string source,
        Func<PeopleCall, Task<string?>> render
    ) => new(suffix, sides, sample, render) { Source = source };

    /// <summary>Reads one source of the person, then prints it; the source is awaited once per render.</summary>
    public static Func<PeopleCall, Task<string?>> Map<T>(
        Func<ViewerData, Task<T>> load,
        Func<T, PeopleCall, string?> print
    ) => async call => print(await load(call.Data), call);

    public TemplateHelperEntry ToEntry(ViewerSide side, string ns)
    {
        string key = ns + "." + Suffix;
        return new TemplateHelperEntry(
            IsPrefix ? key + "<key>" : key,
            [
                TemplateHelperContext.Command,
                TemplateHelperContext.EventResponse,
                TemplateHelperContext.Pipeline,
                TemplateHelperContext.Webhook,
            ],
            new LocalizedText("template.helper." + key.TrimEnd('.')),
            IsPrefix ? key : null
        )
        {
            Sample = side == ViewerSide.Target ? TargetSample ?? Sample : Sample,
            Source = Source,
            Freshness = Freshness,
            Scope =
                side == ViewerSide.User ? TemplateHelperScope.Viewer : TemplateHelperScope.Target,
        };
    }
}
