// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Platform.Enums;

/// <summary>
/// The canonical EventSub <em>wire</em> transport handle (twitch-eventsub §2): how a given subscription /
/// session actually talks to Twitch. Distinct from the deployment-profile selector — a SaaS profile drives
/// both <see cref="Conduit"/> and <see cref="Webhook"/> wire kinds; a self-host profile drives
/// <see cref="WebSocket"/>.
/// </summary>
public enum EventSubTransportKind
{
    WebSocket,
    Conduit,
    Webhook,
}
