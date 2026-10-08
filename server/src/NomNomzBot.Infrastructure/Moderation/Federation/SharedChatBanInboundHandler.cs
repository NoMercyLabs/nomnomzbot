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
using Newtonsoft.Json;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Federation;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Federation.Enums;
using NomNomzBot.Domain.Moderation.Events;

namespace NomNomzBot.Infrastructure.Moderation.Federation;

/// <summary>
/// The moderation-owned inbound handler for <c>moderation.ban.shared</c> (federation-oidc.md §3.7). Federation
/// routes; moderation applies: this deserializes the envelope payload into <see cref="SharedChatBanIssuedEvent"/>
/// and applies it through <see cref="ISharedBanService.ApplyInboundFederatedBanAsync"/> (Origin=federation, no
/// Twitch-session gate). Being auto-discovered by the assembly scan, its <see cref="Type"/> becomes part of the
/// closed inbound accept-set — drop the class, the type is accepted; no wiring edit. Fails closed
/// (<c>schema_invalid</c>) on a payload it cannot deserialize into the load-bearing fields.
/// </summary>
public sealed class SharedChatBanInboundHandler(
    ISharedBanService sharedBans,
    ILogger<SharedChatBanInboundHandler> logger
) : IFederationInboundHandler
{
    public string Type => "moderation.ban.shared";

    public string GatingOptInType => FederationOptInType.SharedChatBans;

    public async Task<Result> ApplyAsync(
        Guid peerId,
        Guid targetBroadcasterId,
        FederationEventEnvelope envelope,
        CancellationToken cancellationToken = default
    )
    {
        SharedChatBanIssuedEvent? inbound;
        try
        {
            inbound = JsonConvert.DeserializeObject<SharedChatBanIssuedEvent>(envelope.PayloadJson);
        }
        catch (JsonException)
        {
            inbound = null;
        }

        if (inbound is null || string.IsNullOrWhiteSpace(inbound.TargetTwitchUserId))
            return Result.Failure(
                "Federated ban payload is missing its target Twitch user id.",
                "schema_invalid"
            );

        Result<SharedBanApplicationResult> applied = await sharedBans.ApplyInboundFederatedBanAsync(
            targetBroadcasterId,
            inbound,
            cancellationToken
        );
        // A Twitch ban that could not be placed (e.g. the target is already banned) is NOT a routing failure: the
        // service already reported it to the streamer, and a Failure here would abort the envelope for the
        // channel's other targets. The envelope was accepted; only this channel's local effect did not happen.
        if (applied.IsFailure)
            logger.LogWarning(
                "Federated shared ban not applied for channel {Channel}: {Reason} - {Error}",
                targetBroadcasterId,
                applied.ErrorCode,
                applied.ErrorMessage
            );
        return Result.Success();
    }
}
