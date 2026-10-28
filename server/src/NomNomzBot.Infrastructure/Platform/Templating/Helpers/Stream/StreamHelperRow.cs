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
using NomNomzBot.Application.Dashboard.Dtos;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

/// <summary>What a stream or channel helper sees: its rendered arguments, the render scope and the render's data.</summary>
internal readonly record struct StreamHelperCall(
    HelperNode Node,
    TemplateRenderScope Scope,
    StreamHelperData Data,
    CancellationToken Ct
)
{
    public IReadOnlyList<string> Args => Node.Arguments;

    public ChannelContext? Channel => Scope.Channel;

    /// <summary>The culture of the channel language (English when unset or unknown).</summary>
    public CultureInfo Culture => NumberCulture.For(Scope.Language);

    /// <summary>How long the channel has been live; null when it is offline or its start is unknown.</summary>
    public TimeSpan? Uptime =>
        Channel is { IsLive: true, WentLiveAt: { } since }
            ? TimeSpan.FromTicks(Math.Max(0, (Scope.Now - since).Ticks))
            : null;
}

/// <summary>
/// One <c>stream.*</c> or <c>channel.*</c> helper form: its registry row, the source it fetches and how it renders.
/// A null result prints the token as written. Every row needs a channel in the scope and the exact argument count
/// it declares; a render without a channel or with another argument count prints as written.
/// </summary>
internal sealed record StreamHelperRow(
    string Key,
    string Sample,
    string? Source,
    string? Metric,
    Func<StreamHelperCall, Task> Warm,
    Func<StreamHelperCall, Task<string?>> Render,
    TemplateHelperFreshness Freshness
)
{
    private static readonly Task Done = Task.CompletedTask;

    /// <summary>A form read from the channel's live in-memory state: nothing is fetched.</summary>
    public static StreamHelperRow Memory(
        string key,
        string sample,
        Func<ChannelContext, StreamHelperCall, string?> render,
        int args = 0
    ) =>
        new(
            key,
            sample,
            null,
            null,
            _ => Done,
            call =>
                Task.FromResult(
                    call.Channel is { } channel && call.Args.Count == args
                        ? render(channel, call)
                        : null
                ),
            TemplateHelperFreshness.Memory
        );

    /// <summary>A form read from one metric of the stream stats service; one call serves every metric a render needs.</summary>
    public static StreamHelperRow Stats(
        string key,
        string sample,
        string metric,
        Func<StreamStatsDto, StreamHelperCall, string?> render,
        int args = 0
    ) =>
        new(
            key,
            sample,
            StreamSources.Stats,
            metric,
            _ => Done,
            async call =>
                call.Args.Count == args
                && await call.Data.StatsAsync([metric], call.Ct) is { } stats
                    ? render(stats, call)
                    : null,
            TemplateHelperFreshness.Cached
        );

    /// <summary>A form over a fetched reference value; <paramref name="read"/> is the render's one memoized fetch of it.</summary>
    public static StreamHelperRow Read<T>(
        string key,
        string sample,
        string source,
        Func<StreamHelperCall, Task<T?>> read,
        Func<T, StreamHelperCall, string?> render,
        int args = 0
    )
        where T : class =>
        ReadAsync(
            key,
            sample,
            source,
            read,
            (value, call) => Task.FromResult(render(value, call)),
            args
        );

    /// <summary>A form over a fetched reference value whose text needs a further fetch.</summary>
    public static StreamHelperRow ReadAsync<T>(
        string key,
        string sample,
        string source,
        Func<StreamHelperCall, Task<T?>> read,
        Func<T, StreamHelperCall, Task<string?>> render,
        int args = 0
    )
        where T : class =>
        new(
            key,
            sample,
            source,
            null,
            call => read(call),
            async call =>
                call.Args.Count == args && await read(call) is { } value
                    ? await render(value, call)
                    : null,
            TemplateHelperFreshness.Cached
        );

    /// <summary>A form over a fetched number (a count the platform reports).</summary>
    public static StreamHelperRow ReadNumber(
        string key,
        string sample,
        string source,
        Func<StreamHelperCall, Task<int?>> read,
        Func<int, StreamHelperCall, string?> render
    ) =>
        new(
            key,
            sample,
            source,
            null,
            call => read(call),
            async call =>
                call.Args.Count == 0 && await read(call) is { } value ? render(value, call) : null,
            TemplateHelperFreshness.Cached
        );

    public TemplateHelperEntry ToEntry() =>
        new(Key, TemplateHelperContexts.Standard, new LocalizedText("template.helper." + Key))
        {
            Sample = Sample,
            Source = Source,
            Freshness = Freshness,
        };
}
