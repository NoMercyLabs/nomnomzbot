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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Infrastructure.Identity;

/// <inheritdoc />
public sealed class ChannelDirectoryService : IChannelDirectoryService
{
    private const int TwitchHitLimit = 20;

    private readonly IChannelService _channels;
    private readonly ITwitchSearchApi _search;

    public ChannelDirectoryService(IChannelService channels, ITwitchSearchApi search)
    {
        _channels = channels;
        _search = search;
    }

    public async Task<Result<PagedList<PublicChannelDto>>> SearchAsync(
        string? query,
        PaginationParams pagination,
        CancellationToken cancellationToken = default
    )
    {
        Result<PagedList<PublicChannelDto>> onBot = await _channels.SearchPublicChannelsAsync(
            query,
            pagination,
            cancellationToken
        );
        string needle = query?.Trim() ?? string.Empty;
        if (onBot.IsFailure || needle.Length == 0 || pagination.Page != 1)
            return onBot;

        Result<TwitchPage<TwitchSearchChannel>> hits = await _search.SearchChannelsAsync(
            needle,
            liveOnly: null,
            new TwitchPageRequest(PageSize: TwitchHitLimit),
            cancellationToken
        );
        // A Helix failure degrades to the bot channels only.
        if (hits.IsFailure)
            return onBot;

        HashSet<string> botLogins = new(
            onBot.Value.Items.Select(c => c.Login),
            StringComparer.OrdinalIgnoreCase
        );
        List<PublicChannelDto> offBot =
        [
            .. hits
                .Value.Items.Where(h => !botLogins.Contains(h.BroadcasterLogin))
                .Select(ToOffBot),
        ];

        List<PublicChannelDto> items = [.. onBot.Value.Items, .. offBot];
        PagedList<PublicChannelDto> page = new(
            items,
            pagination.Page,
            pagination.PageSize,
            onBot.Value.TotalCount + offBot.Count
        );
        return Result.Success(page);
    }

    private static PublicChannelDto ToOffBot(TwitchSearchChannel hit) =>
        new(
            hit.Id,
            hit.BroadcasterLogin,
            hit.DisplayName,
            string.IsNullOrEmpty(hit.ThumbnailUrl) ? null : hit.ThumbnailUrl,
            hit.IsLive,
            "twitch",
            OnBot: false
        );
}
