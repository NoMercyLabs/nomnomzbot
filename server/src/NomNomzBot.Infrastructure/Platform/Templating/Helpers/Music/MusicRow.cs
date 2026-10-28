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
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Music;

/// <summary>What a render of one music token needs.</summary>
internal readonly record struct MusicCall(
    MusicData Data,
    IReadOnlyList<string> Args,
    TemplateRenderScope Scope,
    CancellationToken Ct
)
{
    public DateTimeOffset Now => Scope.Now;

    public string? Timezone => Scope.Timezone;

    public CultureInfo Culture => NumberCulture.For(Scope.Language);
}

internal sealed record MusicRow(
    string Key,
    string Sample,
    MusicSources Sources,
    int MinArgs,
    int MaxArgs,
    Func<MusicCall, Task<string?>> Render
)
{
    /// <summary>Whose data the row reads (the viewer who typed the command, the target, or the channel).</summary>
    public TemplateHelperScope Scope { get; init; } = TemplateHelperScope.Channel;

    public TemplateHelperFreshness Freshness =>
        Sources switch
        {
            MusicSources.None => TemplateHelperFreshness.Pure,
            MusicSources.Profile => TemplateHelperFreshness.Live,
            _ when (Sources & ~MusicSources.Snapshot & ~MusicSources.State) == 0 =>
                TemplateHelperFreshness.Memory,
            _ => TemplateHelperFreshness.Cached,
        };

    /// <summary>A row over the track that plays now; it prints nothing when no track plays.</summary>
    public static MusicRow Now(
        string key,
        string sample,
        Func<NowPlaying, MusicCall, string?> print,
        int maxArgs = 0
    ) =>
        new(
            key,
            sample,
            MusicSources.NowPlaying,
            0,
            maxArgs,
            async call => await call.Data.NowPlayingAsync() is { } now ? print(now, call) : null
        );

    /// <summary>A row over the song-request queue (an empty queue is a real answer).</summary>
    public static MusicRow Queue(
        string key,
        string sample,
        Func<IReadOnlyList<MusicQueueItem>, MusicCall, string?> print,
        int args = 0
    ) =>
        new(
            key,
            sample,
            MusicSources.Queue,
            args,
            args,
            async call => print((await call.Data.QueueAsync()).Queue, call)
        );

    /// <summary>A row over the queue snapshot (the numbered, code-carrying view of the same queue).</summary>
    public static MusicRow Snapshot(
        string key,
        string sample,
        Func<IReadOnlyList<SongRequestQueueSnapshotItem>, MusicCall, string?> print,
        MusicSources extra = MusicSources.None
    ) =>
        new(
            key,
            sample,
            MusicSources.Snapshot | extra,
            0,
            0,
            call => Task.FromResult(print(call.Data.Snapshot(), call))
        );

    /// <summary>A row over the recent accepted requests, newest first.</summary>
    public static MusicRow History(
        string key,
        string sample,
        Func<IReadOnlyList<SongHistoryItemDto>, MusicCall, string?> print,
        int args = 0
    ) =>
        new(
            key,
            sample,
            MusicSources.History,
            args,
            args,
            async call => print(await call.Data.HistoryAsync(), call)
        );

    /// <summary>A row over the channel's song-request settings.</summary>
    public static MusicRow Config(string key, string sample, Func<MusicConfigDto, string?> print) =>
        new(
            key,
            sample,
            MusicSources.Config,
            0,
            0,
            async call => await call.Data.ConfigAsync() is { } config ? print(config) : null
        );

    /// <summary>A row that reads its sources itself.</summary>
    public static MusicRow Custom(
        string key,
        string sample,
        MusicSources sources,
        Func<MusicCall, Task<string?>> render,
        int minArgs = 0,
        int maxArgs = 0
    ) => new(key, sample, sources, minArgs, maxArgs, render);

    public MusicRow ForViewer(TemplateHelperScope scope) => this with { Scope = scope };

    public Task<string?> RenderAsync(MusicCall call) =>
        call.Args.Count < MinArgs || call.Args.Count > MaxArgs
            ? Task.FromResult<string?>(null)
            : Render(call);

    public TemplateHelperEntry ToEntry() =>
        new(Key, TemplateHelperContexts.Standard, new LocalizedText("template.helper." + Key))
        {
            Sample = Sample,
            Source = Sources == MusicSources.None ? null : Sources.ToString().ToLowerInvariant(),
            Freshness = Freshness,
            Scope = Scope,
        };
}
