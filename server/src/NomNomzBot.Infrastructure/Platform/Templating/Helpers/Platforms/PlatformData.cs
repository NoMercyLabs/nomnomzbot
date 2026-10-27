// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Stream;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Platforms;

[Flags]
internal enum PlatformSources
{
    None = 0,
    Links = 1,
    Status = 2,
    Followers = 4,
    Summary = 8,
}

/// <summary>
/// The platform data of one render. Every read starts once, on the first ask, and every row shares its answer.
/// </summary>
internal sealed class PlatformData
{
    private const string ItemKey = "platform.data";

    private readonly IServiceScopeFactory _scopes;
    private readonly IPlatformDirectory _directory;
    private readonly TemplateRenderScope _scope;
    private readonly Guid _broadcasterId;
    private readonly Lazy<Task<IReadOnlyList<PlatformLink>>> _links;
    private readonly ConcurrentDictionary<string, Lazy<Task<PlatformStatus>>> _statuses = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Lazy<Task<int?>> _twitchFollowers;
    private readonly Lazy<
        Task<IReadOnlyDictionary<string, ChannelAnalyticsPlatformSummaryDto>>
    > _summary;

    private PlatformData(
        IServiceScopeFactory scopes,
        IPlatformDirectory directory,
        TemplateRenderScope scope,
        Guid broadcasterId
    )
    {
        _scopes = scopes;
        _directory = directory;
        _scope = scope;
        _broadcasterId = broadcasterId;
        _links = new(LoadLinksAsync);
        _twitchFollowers = new(LoadTwitchFollowersAsync);
        _summary = new(LoadSummaryAsync);
    }

    /// <summary>The render's one platform data; null when the render has no channel.</summary>
    public static PlatformData? For(
        TemplateRenderScope scope,
        IServiceScopeFactory scopes,
        IPlatformDirectory directory
    )
    {
        if (scope.BroadcasterId is not { } broadcasterId)
            return null;

        if (scope.Items.TryGetValue(ItemKey, out object? kept) && kept is PlatformData data)
            return data;

        PlatformData created = new(scopes, directory, scope, broadcasterId);
        scope.Items[ItemKey] = created;
        return created;
    }

    /// <summary>The platform of the person who typed the command (seed <c>user.provider</c>).</summary>
    public string UserProvider => Seed("user.provider");

    /// <summary>The platform the event came from (seed <c>provider</c>).</summary>
    public string EventProvider => Seed("provider");

    public Task<IReadOnlyList<PlatformLink>> LinksAsync() => _links.Value;

    public async Task<PlatformLink?> LinkAsync(string provider) =>
        (await LinksAsync()).FirstOrDefault(l =>
            string.Equals(l.Provider, provider, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>True when the channel is live, so a stream-to-date total has a start.</summary>
    public bool HasStreamWindow => _scope.Channel?.WentLiveAt is not null;

    /// <summary>The connection of one platform with its status; null when the platform is not connected.</summary>
    public async Task<(PlatformLink Link, PlatformStatus Status)?> ConnectionAsync(
        string provider
    ) => await LinkAsync(provider) is { } link ? (link, await StatusAsync(link)) : null;

    public Task<PlatformStatus> StatusAsync(PlatformLink link) =>
        _statuses.GetOrAdd(link.Provider, _ => new(() => LoadStatusAsync(link))).Value;

    /// <summary>Every connection with its status, in platform order.</summary>
    public async Task<IReadOnlyList<(PlatformLink Link, PlatformStatus Status)>> StatusesAsync()
    {
        IReadOnlyList<PlatformLink> links = await LinksAsync();
        PlatformStatus[] statuses = await Task.WhenAll(links.Select(StatusAsync));
        return [.. links.Select((link, i) => (link, statuses[i]))];
    }

    /// <summary>The Twitch follower count; null when Twitch is not connected or the read fails.</summary>
    public Task<int?> TwitchFollowersAsync() => _twitchFollowers.Value;

    /// <summary>The stream-to-date totals of one platform; null when the channel is offline or the platform has none.</summary>
    public async Task<ChannelAnalyticsPlatformSummaryDto?> SummaryAsync(string provider) =>
        (await _summary.Value).GetValueOrDefault(provider);

    /// <summary>Starts the reads of the given sources side by side.</summary>
    public Task WarmAsync(PlatformSources sources)
    {
        List<Task> started = [];
        if (sources.HasFlag(PlatformSources.Links))
            started.Add(LinksAsync());
        if (sources.HasFlag(PlatformSources.Status))
            started.Add(StatusesAsync());
        if (sources.HasFlag(PlatformSources.Followers))
            started.Add(TwitchFollowersAsync());
        if (sources.HasFlag(PlatformSources.Summary))
            started.Add(_summary.Value);
        return Task.WhenAll(started);
    }

    private string Seed(string key) =>
        _scope.Variables.TryGetValue(key, out string? value) ? value : string.Empty;

    private async Task<IReadOnlyList<PlatformLink>> LoadLinksAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        return await _directory.ListAsync(
            scope.ServiceProvider,
            _broadcasterId,
            CancellationToken.None
        );
    }

    private async Task<PlatformStatus> LoadStatusAsync(PlatformLink link)
    {
        using IServiceScope scope = _scopes.CreateScope();
        return await PlatformStatusReader.ReadAsync(
            scope.ServiceProvider,
            link,
            link.ChannelId == _broadcasterId ? _scope.Channel : null,
            CancellationToken.None
        );
    }

    private async Task<int?> LoadTwitchFollowersAsync()
    {
        if (await LinkAsync(AuthEnums.Platform.Twitch) is not { } twitch)
            return null;

        using IServiceScope scope = _scopes.CreateScope();
        Result<int> count = await scope
            .ServiceProvider.GetRequiredService<ITwitchChannelsApi>()
            .GetChannelFollowerCountAsync(twitch.ChannelId, CancellationToken.None);
        return count.IsSuccess ? count.Value : null;
    }

    private async Task<
        IReadOnlyDictionary<string, ChannelAnalyticsPlatformSummaryDto>
    > LoadSummaryAsync()
    {
        if (_scope.Channel?.WentLiveAt is not { } wentLive)
            return new Dictionary<string, ChannelAnalyticsPlatformSummaryDto>();

        TimeZoneInfo zone = StreamText.ZoneOf(_scope.Timezone);
        DateOnly from = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(wentLive, zone).DateTime);
        DateOnly to = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_scope.Now, zone).DateTime);

        using IServiceScope scope = _scopes.CreateScope();
        Result<IReadOnlyList<ChannelAnalyticsPlatformSummaryDto>> summary = await scope
            .ServiceProvider.GetRequiredService<IChannelAnalyticsService>()
            .GetPlatformSummaryAsync(_broadcasterId, from, to, CancellationToken.None);
        return summary.IsSuccess
            ? summary.Value.ToDictionary(s => s.Provider, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ChannelAnalyticsPlatformSummaryDto>();
    }
}
