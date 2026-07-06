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
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>
/// Sends chat AS THE LOGGED-IN OPERATOR (chat-client.md §3.3). It resolves the operator's own Twitch user id for
/// the <c>sender_id</c> and rides the operator token via <see cref="TwitchHelixAuth.Operator"/> (the transport
/// resolves it from <c>OperatorUserId</c>), targeting the tenant's Twitch channel. Both ids come from the
/// canonical <see cref="ITwitchIdentityResolver"/> (Guid → Twitch string id), keeping the hard invariant that
/// Twitch never receives a Guid. This is the composer's send path; the bot's
/// <see cref="Domain.Chat.Interfaces.IChatProvider"/> stays the automation/announcement path.
/// </summary>
public sealed class OperatorChatSender : IOperatorChatSender
{
    private readonly ITwitchHelixTransport _transport;
    private readonly ITwitchIdentityResolver _identity;
    private readonly ILogger<OperatorChatSender> _logger;

    public OperatorChatSender(
        ITwitchHelixTransport transport,
        ITwitchIdentityResolver identity,
        ILogger<OperatorChatSender> logger
    )
    {
        _transport = transport;
        _identity = identity;
        _logger = logger;
    }

    public async Task<Result> SendAsUserAsync(
        Guid operatorUserId,
        Guid broadcasterId,
        string message,
        string? replyToMessageId,
        CancellationToken ct = default
    )
    {
        string? targetChannelId = await _identity.GetTwitchChannelIdAsync(broadcasterId, ct);
        if (targetChannelId is null)
        {
            _logger.LogWarning(
                "OperatorChatSender: no Twitch channel id for tenant {BroadcasterId}, skipping",
                broadcasterId
            );
            return Result.Failure("Channel is not known locally.", TwitchErrorCodes.NotFound);
        }

        // sender_id is the operator's OWN Twitch account id (Users.TwitchUserId) — the same account whose token
        // the transport resolves for Auth.Operator, so Twitch accepts the send as that user.
        string? senderId = await _identity.GetTwitchUserIdAsync(operatorUserId, ct);
        if (string.IsNullOrEmpty(senderId))
        {
            return Result.Failure(
                "You have no linked Twitch identity to send as.",
                TwitchErrorCodes.NoToken
            );
        }

        TwitchHelixRequest request = new(
            HttpMethod.Post,
            "chat/messages",
            TwitchHelixAuth.Operator,
            Body: new
            {
                BroadcasterId = targetChannelId,
                SenderId = senderId,
                Message = message,
                ReplyParentMessageId = replyToMessageId,
            },
            Priority: TwitchCallPriority.UserInteractive,
            OperatorUserId: operatorUserId
        );

        Result result = await _transport.SendAsync(request, ct);
        if (result.IsFailure)
        {
            _logger.LogWarning(
                "OperatorChatSender: send by {OperatorUserId} to {BroadcasterId} failed: {Error}",
                operatorUserId,
                broadcasterId,
                result.ErrorMessage
            );
        }

        return result;
    }
}
