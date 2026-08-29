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
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Application.Contracts.Platform;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Platform.ChannelOps;

/// <summary>
/// The Kick half of the channel-ops seam (S027): title/category writes ride the streamer's own vaulted
/// Kick token via <c>PATCH /public/v1/channels</c>. Kick's PATCH keys categories by numeric id, not name,
/// so a category NAME is resolved first through a channel read (<see cref="IKickApiClient.GetChannelAsync"/>)
/// — Kick's public API has no category-search endpoint, so an unresolvable name is rejected rather than
/// guessed. Kick has no stream-tags concept: a request carrying tags is rejected (<c>VALIDATION_FAILED</c>),
/// mirroring how <see cref="YouTubePlatformApi"/> rejects fields its platform cannot represent.
/// </summary>
public sealed class KickPlatformApi : IPlatformApi
{
    private readonly IKickAccessTokenProvider _tokens;
    private readonly IKickApiClient _client;

    public KickPlatformApi(IKickAccessTokenProvider tokens, IKickApiClient client)
    {
        _tokens = tokens;
        _client = client;
    }

    public string Provider => AuthEnums.Platform.Kick;

    public async Task<Result<PlatformStreamInfoApplied>> UpdateStreamInfoAsync(
        Guid broadcasterId,
        PlatformStreamInfoUpdate update,
        CancellationToken cancellationToken = default
    )
    {
        if (update.Tags is { Count: > 0 })
            return Result.Failure<PlatformStreamInfoApplied>(
                "Kick channels have no stream tags to set.",
                "VALIDATION_FAILED"
            );
        if (update.Title is null && update.CategoryName is null)
            return Result.Failure<PlatformStreamInfoApplied>(
                "Nothing to update.",
                "VALIDATION_FAILED"
            );

        KickAccess? access = await _tokens.GetAsync(broadcasterId, cancellationToken);
        if (access is null)
            return Result.Failure<PlatformStreamInfoApplied>(
                "No usable Kick token for this channel.",
                "MISSING_SCOPE"
            );

        int? categoryId = null;
        string? resolvedCategory = update.CategoryName;
        if (update.CategoryName is not null)
        {
            Result<KickChannel> current = await _client.GetChannelAsync(
                access.AccessToken,
                access.BroadcasterUserId,
                cancellationToken
            );
            if (current.IsFailure)
                return Result.Failure<PlatformStreamInfoApplied>(
                    current.ErrorMessage!,
                    current.ErrorCode,
                    current.ErrorDetail
                );

            // Kick's public API has no category-search endpoint — a category id can only be carried
            // forward from the current channel read (an exact-name match) or left unresolved, in which
            // case the caller's requested category cannot be applied and the update is rejected honestly
            // rather than silently dropped.
            if (
                string.Equals(
                    current.Value.CategoryName,
                    update.CategoryName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                categoryId = current.Value.CategoryId;
                resolvedCategory = current.Value.CategoryName;
            }
            else
            {
                return Result.Failure<PlatformStreamInfoApplied>(
                    $"Kick category '{update.CategoryName}' could not be resolved to a category id.",
                    "VALIDATION_FAILED"
                );
            }
        }

        Result applied = await _client.UpdateChannelAsync(
            access.AccessToken,
            update.Title,
            categoryId,
            cancellationToken
        );
        if (applied.IsFailure)
            return Result.Failure<PlatformStreamInfoApplied>(
                applied.ErrorMessage!,
                applied.ErrorCode,
                applied.ErrorDetail
            );

        return Result.Success(
            new PlatformStreamInfoApplied(update.Title, resolvedCategory, Tags: null)
        );
    }
}
