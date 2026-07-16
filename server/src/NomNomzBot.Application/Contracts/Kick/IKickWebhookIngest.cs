// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Contracts.Kick;

/// <summary>
/// Turns VERIFIED Kick webhook bodies into their canonical effects. <c>chat.message.sent</c> becomes the
/// canonical <c>ChatMessageReceivedEvent</c> (<c>Provider = kick</c>) — the same one substrate Twitch and
/// YouTube chat ride, so persistence, the dashboard push, commands, and the analytics projections all fire
/// with zero Kick-specific consumers. <c>livestream.status.updated</c> is Kick's live tracker: it stamps
/// the Kick tenant's <c>Channel.IsLive</c> (+ title) that the dashboard's <c>platformsLive</c> aggregates.
/// The caller (webhook controller) owns signature + freshness checks; this owns tenant resolution,
/// redelivery dedupe, and the payload translation.
/// </summary>
public interface IKickWebhookIngest
{
    Task HandleChatMessageAsync(string rawBody, CancellationToken cancellationToken = default);

    Task HandleLivestreamStatusAsync(string rawBody, CancellationToken cancellationToken = default);
}
