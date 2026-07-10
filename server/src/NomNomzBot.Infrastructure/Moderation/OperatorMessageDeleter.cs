// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Transport;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Moderation.Services;

namespace NomNomzBot.Infrastructure.Moderation;

/// <summary>
/// Deletes a chat message AS THE LOGGED-IN OPERATOR (chat-client.md §3.5). It resolves the tenant's Twitch channel id
/// (Guid → Twitch string id via the canonical <see cref="ITwitchIdentityResolver"/>, keeping the invariant that
/// Twitch never receives a Guid) and hands the raw Twitch id to
/// <see cref="ITwitchModerationApi.DeleteChatMessageAsOperatorAsync"/>, which rides the operator's OWN token
/// (<see cref="TwitchHelixAuth.Operator"/>) and sets <c>moderator_id</c> to the operator's Twitch id — so the removal
/// is attributed to the moderator, not the broadcaster. The bot's <see cref="Domain.Chat.Interfaces.IChatProvider"/>
/// stays the automation path (pipeline / AutoMod), which deletes under the tenant token.
/// </summary>
public sealed class OperatorMessageDeleter : IOperatorMessageDeleter
{
    private readonly ITwitchModerationApi _moderation;
    private readonly ITwitchIdentityResolver _identity;
    private readonly ILogger<OperatorMessageDeleter> _logger;

    public OperatorMessageDeleter(
        ITwitchModerationApi moderation,
        ITwitchIdentityResolver identity,
        ILogger<OperatorMessageDeleter> logger
    )
    {
        _moderation = moderation;
        _identity = identity;
        _logger = logger;
    }

    public async Task<Result> DeleteAsUserAsync(
        Guid operatorUserId,
        Guid broadcasterId,
        string messageId,
        CancellationToken ct = default
    )
    {
        string? targetChannelId = await _identity.GetTwitchChannelIdAsync(broadcasterId, ct);
        if (targetChannelId is null)
        {
            _logger.LogWarning(
                "OperatorMessageDeleter: no Twitch channel id for tenant {BroadcasterId}, skipping",
                broadcasterId
            );
            return Result.Failure("Channel is not known locally.", TwitchErrorCodes.NotFound);
        }

        // The operator's own Twitch id (moderator_id) and token are resolved inside the sub-client from operatorUserId;
        // a missing linked identity comes back as no_token, which the caller uses to fall back rather than mis-attribute.
        Result result = await _moderation.DeleteChatMessageAsOperatorAsync(
            operatorUserId,
            targetChannelId,
            messageId,
            ct
        );
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "OperatorMessageDeleter: delete of {MessageId} in {BroadcasterId} by {OperatorUserId} failed: {Error}",
                messageId,
                broadcasterId,
                operatorUserId,
                result.ErrorMessage
            );
        }

        return result;
    }
}
