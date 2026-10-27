// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Music;

/// <summary>The reads a music token can need; a row names the ones it uses so a render fetches each once.</summary>
[Flags]
internal enum MusicSources
{
    None = 0,
    NowPlaying = 1,
    Queue = 2,
    Snapshot = 4,
    History = 8,
    Config = 16,
    Provider = 32,
    State = 64,
    Profile = 128,
}

/// <summary>
/// The music reads of ONE render. Each source is fetched at most once, however many tokens use it, and is kept
/// in the render's item bag so a later token finds the same result.
/// </summary>
internal sealed class MusicData
{
    private const string ItemKey = "music.data";

    /// <summary>How many recent requests the history reads cover (one call per render).</summary>
    public const int HistoryWindow = 500;

    private readonly IServiceScopeFactory _scopes;
    private readonly IViewerDirectory _directory;
    private readonly Guid _broadcasterId;
    private readonly IReadOnlyDictionary<string, string> _seeds;
    private readonly Lazy<Task<NowPlaying?>> _now;
    private readonly Lazy<Task<MusicQueue>> _queue;
    private readonly Lazy<IReadOnlyList<SongRequestQueueSnapshotItem>> _snapshot;
    private readonly Lazy<Task<IReadOnlyList<SongHistoryItemDto>>> _history;
    private readonly Lazy<Task<MusicConfigDto?>> _config;
    private readonly Lazy<Task<string?>> _provider;
    private readonly Lazy<Task<long?>> _totalRequests;

    private MusicData(
        IServiceScopeFactory scopes,
        IViewerDirectory directory,
        Guid broadcasterId,
        IReadOnlyDictionary<string, string> seeds
    )
    {
        _scopes = scopes;
        _directory = directory;
        _broadcasterId = broadcasterId;
        _seeds = seeds;
        _now = new Lazy<Task<NowPlaying?>>(LoadNowPlayingAsync);
        _queue = new Lazy<Task<MusicQueue>>(LoadQueueAsync);
        _snapshot = new Lazy<IReadOnlyList<SongRequestQueueSnapshotItem>>(LoadSnapshot);
        _history = new Lazy<Task<IReadOnlyList<SongHistoryItemDto>>>(LoadHistoryAsync);
        _config = new Lazy<Task<MusicConfigDto?>>(LoadConfigAsync);
        _provider = new Lazy<Task<string?>>(LoadProviderAsync);
        _totalRequests = new Lazy<Task<long?>>(LoadTotalRequestsAsync);
    }

    /// <summary>The render's one music data; null when the render has no channel.</summary>
    public static MusicData? For(
        TemplateRenderScope scope,
        IServiceScopeFactory scopes,
        IViewerDirectory directory
    )
    {
        if (scope.BroadcasterId is not { } broadcasterId)
            return null;

        if (scope.Items.TryGetValue(ItemKey, out object? kept) && kept is MusicData data)
            return data;

        MusicData created = new(scopes, directory, broadcasterId, scope.Variables);
        scope.Items[ItemKey] = created;
        return created;
    }

    /// <summary>The display name of the person who typed the command (empty when the render has none).</summary>
    public string User => Seed("user");

    /// <summary>The first mention of the command, without its <c>@</c>.</summary>
    public string Target => Seed("target").TrimStart('@');

    public Task<NowPlaying?> NowPlayingAsync() => _now.Value;

    public Task<MusicQueue> QueueAsync() => _queue.Value;

    public IReadOnlyList<SongRequestQueueSnapshotItem> Snapshot() => _snapshot.Value;

    /// <summary>The recent accepted requests, newest first.</summary>
    public Task<IReadOnlyList<SongHistoryItemDto>> HistoryAsync() => _history.Value;

    public Task<MusicConfigDto?> ConfigAsync() => _config.Value;

    public Task<string?> ProviderAsync() => _provider.Value;

    /// <summary>The viewer's all-time song-request count; null when the person is unknown.</summary>
    public Task<long?> TotalRequestsAsync() => _totalRequests.Value;

    /// <summary>The state of the channel's playback director (a synchronous in-memory read).</summary>
    public PlaybackStateKind State()
    {
        using IServiceScope scope = _scopes.CreateScope();
        return scope
            .ServiceProvider.GetRequiredService<IPlaybackStateReader>()
            .StateOf(_broadcasterId)
            .Kind;
    }

    /// <summary>Starts the reads of the given sources side by side.</summary>
    public Task WarmAsync(MusicSources sources)
    {
        List<Task> started = [];
        if (sources.HasFlag(MusicSources.NowPlaying))
            started.Add(NowPlayingAsync());
        if (sources.HasFlag(MusicSources.Queue))
            started.Add(QueueAsync());
        if (sources.HasFlag(MusicSources.History))
            started.Add(HistoryAsync());
        if (sources.HasFlag(MusicSources.Config))
            started.Add(ConfigAsync());
        if (sources.HasFlag(MusicSources.Provider))
            started.Add(ProviderAsync());
        if (sources.HasFlag(MusicSources.Profile))
            started.Add(TotalRequestsAsync());
        return Task.WhenAll(started);
    }

    private string Seed(string key) =>
        _seeds.TryGetValue(key, out string? value) ? value : string.Empty;

    private string Id => _broadcasterId.ToString();

    private async Task<NowPlaying?> LoadNowPlayingAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        IMusicService music = scope.ServiceProvider.GetRequiredService<IMusicService>();
        return await music.GetNowPlayingAsync(Id);
    }

    private async Task<MusicQueue> LoadQueueAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        IMusicService music = scope.ServiceProvider.GetRequiredService<IMusicService>();
        return await music.GetQueueAsync(Id);
    }

    private IReadOnlyList<SongRequestQueueSnapshotItem> LoadSnapshot()
    {
        using IServiceScope scope = _scopes.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IMusicService>().SnapshotQueue(Id);
    }

    private async Task<IReadOnlyList<SongHistoryItemDto>> LoadHistoryAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        IMusicHistoryReader reader =
            scope.ServiceProvider.GetRequiredService<IMusicHistoryReader>();
        Result<List<SongHistoryItemDto>> recent = await reader.GetRecentAsync(
            _broadcasterId,
            HistoryWindow
        );
        return recent.IsSuccess ? recent.Value : [];
    }

    private async Task<MusicConfigDto?> LoadConfigAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        IMusicConfigService config =
            scope.ServiceProvider.GetRequiredService<IMusicConfigService>();
        Result<MusicConfigDto> found = await config.GetConfigAsync(Id);
        return found.IsSuccess ? found.Value : null;
    }

    private async Task<string?> LoadProviderAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        IMusicService music = scope.ServiceProvider.GetRequiredService<IMusicService>();
        return await music.GetActiveProviderKeyAsync(Id);
    }

    private async Task<long?> LoadTotalRequestsAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        ViewerRef? who = await _directory.ResolveAsync(
            scope.ServiceProvider,
            ViewerSide.User,
            _seeds,
            CancellationToken.None
        );
        if (who?.UserId is not { } userId)
            return null;

        IViewerAnalyticsService analytics =
            scope.ServiceProvider.GetRequiredService<IViewerAnalyticsService>();
        Result<ViewerProfileDto> profile = await analytics.GetProfileAsync(_broadcasterId, userId);
        return profile.IsSuccess ? profile.Value.TotalSongRequests : null;
    }
}
