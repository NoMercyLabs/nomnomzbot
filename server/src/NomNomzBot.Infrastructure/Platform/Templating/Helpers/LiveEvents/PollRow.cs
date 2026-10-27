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
using NomNomzBot.Application.Community.Dtos;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.LiveEvents;

internal readonly record struct PollCall(
    HelperNode Node,
    TemplateRenderScope Scope,
    LivePollData Data,
    CancellationToken Ct
)
{
    public IReadOnlyList<string> Args => Node.Arguments;
}

/// <summary>
/// One <c>poll.*</c> form. Its text is empty while no poll is running (or the source is down), and null (the
/// token as written) for an argument that is not valid.
/// </summary>
internal sealed record PollRow(
    string Key,
    string Sample,
    string Source,
    Func<PollCall, Task> Warm,
    Func<PollCall, Task<string?>> Render
)
{
    private const string ActiveSource = "poll.active";
    private const string HistorySource = "poll.history";

    /// <summary>A form over the running poll; <paramref name="render"/> only runs while one is running.</summary>
    public static PollRow Active(
        string key,
        string sample,
        Func<TwitchPoll, PollCall, string?> render,
        int args = 0
    ) => State(key, sample, (poll, call) => poll is null ? string.Empty : render(poll, call), args);

    /// <summary>A form that also speaks when no poll is running (<c>poll.isActive</c>).</summary>
    public static PollRow State(
        string key,
        string sample,
        Func<TwitchPoll?, PollCall, string?> render,
        int args = 0
    ) =>
        new(
            key,
            sample,
            ActiveSource,
            call => call.Data.ActiveAsync(call.Ct),
            async call =>
                call.Args.Count == args ? render(await call.Data.ActiveAsync(call.Ct), call) : null
        );

    /// <summary>A form over the newest finished poll of the history.</summary>
    public static PollRow Last(
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
        new(
            Key,
            [.. Enum.GetValues<TemplateHelperContext>()],
            new LocalizedText("template.helper." + Key)
        )
        {
            Sample = Sample,
            Source = Source,
            Freshness = TemplateHelperFreshness.Cached,
        };
}

internal interface IPollHelperGroup
{
    IReadOnlyList<PollRow> Rows { get; }
}

internal abstract class PollHelperGroup(params PollRow[] rows) : IPollHelperGroup
{
    public IReadOnlyList<PollRow> Rows { get; } = rows;
}
