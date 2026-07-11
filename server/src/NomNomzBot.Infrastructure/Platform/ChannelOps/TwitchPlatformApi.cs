// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Platform.ChannelOps;

/// <summary>
/// The Twitch half of the channel-ops seam: a category NAME resolves through Twitch search to its
/// catalogue id + canonical spelling (exact-name match wins over the first fuzzy hit), then the change
/// rides Helix Modify Channel Information on the broadcaster's own token. An unresolvable category keeps
/// the user's string and sends no game id — Twitch's pre-seam behavior, moved verbatim out of
/// <c>StreamController</c>.
/// </summary>
public sealed class TwitchPlatformApi : IPlatformApi
{
    private readonly ITwitchChannelsApi _channels;
    private readonly ITwitchSearchApi _search;

    public TwitchPlatformApi(ITwitchChannelsApi channels, ITwitchSearchApi search)
    {
        _channels = channels;
        _search = search;
    }

    public string Provider => AuthEnums.Platform.Twitch;

    public async Task<Result<PlatformStreamInfoApplied>> UpdateStreamInfoAsync(
        Guid broadcasterId,
        PlatformStreamInfoUpdate update,
        CancellationToken cancellationToken = default
    )
    {
        string? gameId = null;
        string? resolvedCategory = update.CategoryName;
        if (update.CategoryName is not null)
        {
            Result<TwitchPage<TwitchSearchCategory>> search = await _search.SearchCategoriesAsync(
                update.CategoryName,
                new TwitchPageRequest(),
                cancellationToken
            );
            IReadOnlyList<TwitchSearchCategory> categories = search.IsSuccess
                ? search.Value.Items
                : [];
            TwitchSearchCategory? match =
                categories.FirstOrDefault(c =>
                    string.Equals(c.Name, update.CategoryName, StringComparison.OrdinalIgnoreCase)
                ) ?? categories.FirstOrDefault();

            if (match is not null)
            {
                gameId = match.Id;
                resolvedCategory = match.Name;
            }
        }

        Result applied = await _channels.ModifyChannelInformationAsync(
            broadcasterId,
            new ModifyChannelInformationRequest(
                Title: update.Title,
                GameId: gameId,
                Tags: update.Tags
            ),
            cancellationToken
        );
        if (applied.IsFailure)
            return Result.Failure<PlatformStreamInfoApplied>(
                applied.ErrorMessage!,
                applied.ErrorCode,
                applied.ErrorDetail
            );

        return Result.Success(
            new PlatformStreamInfoApplied(update.Title, resolvedCategory, update.Tags)
        );
    }
}
