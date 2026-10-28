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
