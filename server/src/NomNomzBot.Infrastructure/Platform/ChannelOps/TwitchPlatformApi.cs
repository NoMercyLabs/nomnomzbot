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
                new(),
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
            new(
                Title: update.Title,
                GameId: gameId,
                Tags: update.Tags,
                ContentClassificationLabels: ToLabelChoices(update.ContentLabels),
                IsBrandedContent: update.IsBrandedContent
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

    // Twitch turns a label off only when it is sent with is_enabled false, so every editable label goes out.
    private static List<TwitchContentClassificationLabelChoice>? ToLabelChoices(
        IReadOnlyList<string>? enabled
    )
    {
        if (enabled is null)
            return null;

        return
        [
            .. TwitchContentLabels.Editable.Select(id => new TwitchContentClassificationLabelChoice(
                id,
                enabled.Contains(id)
            )),
        ];
    }
}
