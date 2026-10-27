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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Application.Quotes.Dtos;
using NomNomzBot.Application.Quotes.Services;
using NomNomzBot.Application.ViewerData.Services;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.People;

/// <summary>
/// What one person of one render can be asked: every source is read at most once per render, and only when a
/// row asks for it. A person with no record (or a source that fails) reads as null, never as an invented value.
/// </summary>
internal sealed class ViewerData
{
    private readonly IServiceScopeFactory _scopes;
    private readonly Guid _broadcasterId;
    private readonly Lazy<Task<ViewerProfileDto?>> _profile;
    private readonly Lazy<Task<WatchStreakDto?>> _streak;
    private readonly Lazy<Task<FollowLookup>> _follow;
    private readonly Lazy<Task<long?>> _balance;
    private readonly Lazy<Task<CurrencyConfigDto?>> _currency;
    private readonly Lazy<Task<IReadOnlyList<LeaderboardEntryDto>>> _ranking;
    private readonly Lazy<Task<int?>> _quotes;

    public ViewerData(IServiceScopeFactory scopes, Guid broadcasterId, ViewerRef who)
    {
        _scopes = scopes;
        _broadcasterId = broadcasterId;
        Who = who;
        _profile = new Lazy<Task<ViewerProfileDto?>>(LoadProfileAsync);
        _streak = new Lazy<Task<WatchStreakDto?>>(LoadStreakAsync);
        _follow = new Lazy<Task<FollowLookup>>(LoadFollowAsync);
        _balance = new Lazy<Task<long?>>(LoadBalanceAsync);
        _currency = new Lazy<Task<CurrencyConfigDto?>>(LoadCurrencyAsync);
        _ranking = new Lazy<Task<IReadOnlyList<LeaderboardEntryDto>>>(LoadRankingAsync);
        _quotes = new Lazy<Task<int?>>(LoadQuoteCountAsync);
        Twitch = new ViewerTwitchData(scopes, who);
    }

    public ViewerRef Who { get; }

    /// <summary>The Twitch lookups of this person (user record, chat colour, channel, stream).</summary>
    public ViewerTwitchData Twitch { get; }

    public Task<ViewerProfileDto?> ProfileAsync() => _profile.Value;

    public Task<WatchStreakDto?> StreakAsync() => _streak.Value;

    public Task<FollowLookup> FollowAsync() => _follow.Value;

    /// <summary>The wallet balance; null when the person has no wallet in this channel.</summary>
    public Task<long?> BalanceAsync() => _balance.Value;

    public Task<CurrencyConfigDto?> CurrencyAsync() => _currency.Value;

    /// <summary>The channel's balance ranking, best first; empty when the channel has no such board.</summary>
    public Task<IReadOnlyList<LeaderboardEntryDto>> RankingAsync() => _ranking.Value;

    public Task<int?> QuoteCountAsync() => _quotes.Value;

    /// <summary>One stored value of the person (not cached: a key is read by the row that names it).</summary>
    public async Task<string?> DataAsync(string key, CancellationToken ct)
    {
        if (Who.UserId is not { } userId)
            return null;

        using IServiceScope scope = _scopes.CreateScope();
        IViewerDataService data = scope.ServiceProvider.GetRequiredService<IViewerDataService>();
        Result<string?> found = await data.GetAsync(_broadcasterId, userId, key, ct);
        return found.IsSuccess ? found.Value : null;
    }

    private async Task<ViewerProfileDto?> LoadProfileAsync()
    {
        if (Who.UserId is not { } userId)
            return null;

        using IServiceScope scope = _scopes.CreateScope();
        IViewerAnalyticsService analytics =
            scope.ServiceProvider.GetRequiredService<IViewerAnalyticsService>();
        Result<ViewerProfileDto> found = await analytics.GetProfileAsync(_broadcasterId, userId);
        return found.IsSuccess ? found.Value : null;
    }

    private async Task<WatchStreakDto?> LoadStreakAsync()
    {
        if (Who.UserId is not { } userId)
            return null;

        using IServiceScope scope = _scopes.CreateScope();
        IViewerAnalyticsService analytics =
            scope.ServiceProvider.GetRequiredService<IViewerAnalyticsService>();
        Result<WatchStreakDto> found = await analytics.GetStreakAsync(_broadcasterId, userId);
        return found.IsSuccess ? found.Value : null;
    }

    private async Task<FollowLookup> LoadFollowAsync()
    {
        if (Who.PlatformUserId is not { } platformId)
            return FollowLookup.Unknown;

        using IServiceScope scope = _scopes.CreateScope();
        IFollowStateService follows =
            scope.ServiceProvider.GetRequiredService<IFollowStateService>();
        return await follows.ResolveAsync(_broadcasterId, Who.Provider, platformId);
    }

    private async Task<long?> LoadBalanceAsync()
    {
        if (Who.UserId is not { } userId)
            return null;

        using IServiceScope scope = _scopes.CreateScope();
        ICurrencyAccountService accounts =
            scope.ServiceProvider.GetRequiredService<ICurrencyAccountService>();
        Result<long> found = await accounts.GetBalanceAsync(_broadcasterId, userId);
        return found.IsSuccess ? found.Value : null;
    }

    private async Task<CurrencyConfigDto?> LoadCurrencyAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        ICurrencyConfigService config =
            scope.ServiceProvider.GetRequiredService<ICurrencyConfigService>();
        Result<CurrencyConfigDto> found = await config.GetConfigAsync(_broadcasterId);
        return found.IsSuccess ? found.Value : null;
    }

    private async Task<IReadOnlyList<LeaderboardEntryDto>> LoadRankingAsync()
    {
        using IServiceScope scope = _scopes.CreateScope();
        IEconomyLeaderboardService boards =
            scope.ServiceProvider.GetRequiredService<IEconomyLeaderboardService>();
        Result<IReadOnlyList<LeaderboardConfigDto>> configs = await boards.ListConfigsAsync(
            _broadcasterId
        );
        LeaderboardConfigDto? balanceBoard = configs.IsSuccess
            ? configs.Value.FirstOrDefault(c =>
                string.Equals(c.Metric, "balance", StringComparison.OrdinalIgnoreCase)
            )
            : null;
        if (balanceBoard is null)
            return [];

        Result<IReadOnlyList<LeaderboardEntryDto>> ranking = await boards.GetRankingAsync(
            _broadcasterId,
            balanceBoard.Id,
            top: null
        );
        return ranking.IsSuccess ? ranking.Value : [];
    }

    private async Task<int?> LoadQuoteCountAsync()
    {
        if (Who.UserId is not { } userId)
            return null;

        using IServiceScope scope = _scopes.CreateScope();
        IQuoteService quotes = scope.ServiceProvider.GetRequiredService<IQuoteService>();
        Result<PagedList<QuoteDto>> page = await quotes.ListByUserAsync(
            _broadcasterId,
            userId,
            new PaginationParams(PageSize: 1)
        );
        return page.IsSuccess ? page.Value.TotalCount : null;
    }
}
