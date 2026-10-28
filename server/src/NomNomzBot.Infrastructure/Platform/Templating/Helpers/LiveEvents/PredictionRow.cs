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
using NomNomzBot.Application.Community.Dtos;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

internal readonly record struct PredictionCall(
    HelperNode Node,
    TemplateRenderScope Scope,
    LivePredictionData Data,
    CancellationToken Ct
)
{
    public IReadOnlyList<string> Args => Node.Arguments;

    /// <summary>The culture of the channel language (English when unset or unknown).</summary>
    public CultureInfo Culture => NumberCulture.For(Scope.Language);
}

/// <summary>
/// One <c>prediction.*</c> form. Its text is empty while no prediction is running (or the source is down), and
/// null (the token as written) for an argument that is not valid.
/// </summary>
internal sealed record PredictionRow(
    string Key,
    string Sample,
    string Source,
    Func<PredictionCall, Task> Warm,
    Func<PredictionCall, Task<string?>> Render
)
{
    private const string CurrentSource = "prediction.current";
    private const string HistorySource = "prediction.history";

    /// <summary>A form over the unfinished prediction; <paramref name="render"/> only runs while one exists.</summary>
    public static PredictionRow Current(
        string key,
        string sample,
        Func<TwitchPrediction, PredictionCall, string?> render,
        int args = 0
    ) =>
        State(
            key,
            sample,
            (prediction, call) => prediction is null ? string.Empty : render(prediction, call),
            args
        );

    /// <summary>A form that also speaks when no prediction is running (<c>prediction.isActive</c>).</summary>
    public static PredictionRow State(
        string key,
        string sample,
        Func<TwitchPrediction?, PredictionCall, string?> render,
        int args = 0
    ) =>
        new(
            key,
            sample,
            CurrentSource,
            call => call.Data.CurrentAsync(call.Ct),
            async call =>
                call.Args.Count == args ? render(await call.Data.CurrentAsync(call.Ct), call) : null
        );

    /// <summary>A form over the newest finished prediction of the history.</summary>
    public static PredictionRow Last(
        string key,
        string sample,
        Func<PollPredictionHistoryItemDto, string?> render
    ) =>
        new(
            key,
            sample,
            HistorySource,
            call => call.Data.LastAsync(call.Ct),
            async call =>
                call.Args.Count == 0
                    ? await call.Data.LastAsync(call.Ct) is { } last
                        ? render(last)
                        : string.Empty
                    : null
        );

    public TemplateHelperEntry ToEntry() =>
        new(Key, TemplateHelperContexts.Standard, new LocalizedText("template.helper." + Key))
        {
            Sample = Sample,
            Source = Source,
            Freshness = TemplateHelperFreshness.Cached,
        };
}

internal interface IPredictionHelperGroup
{
    IReadOnlyList<PredictionRow> Rows { get; }
}

internal abstract class PredictionHelperGroup(params PredictionRow[] rows) : IPredictionHelperGroup
{
    public IReadOnlyList<PredictionRow> Rows { get; } = rows;
}
