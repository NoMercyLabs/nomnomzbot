// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The hubs are not in v1.json, so their payloads are written here, once, from the server records.
// Wire names are camelCase. Source: server/src/NomNomzBot.Api/Hubs/Dtos/*.cs, Hubs/Clients/*.cs.

// HubResponseDtos.cs: StreamStatusDto
export type StreamStatusPayload = {
  isLive: boolean
  streamId: string | null
  title: string | null
  gameName: string | null
  startedAt: string | null
}

// HubResponseDtos.cs: ModActionDto
export type ModActionPayload = {
  action: string
  moderatorId: string
  targetUserId: string
  reason: string | null
  durationSeconds: number | null
  broadcasterId: string
}

// HubResponseDtos.cs: ConfigChangedDto
export type ConfigChangedPayload = {
  broadcasterId: string
  domain: string
  entityId: string | null
  action: string
}

// HubResponseDtos.cs: CommandExecutedDto. Whether commandName carries the leading "!": not checked, so readers accept both.
export type CommandExecutedPayload = {
  broadcasterId: string
  commandName: string
  triggeredByUserId: string
  succeeded: boolean
  timestamp: string
}

// AlertDtos.cs: ChannelEventDto. The type is the method key the server gives the event.
export type ChannelEventPayload = {
  type: string
  broadcasterId: string
  userId: string | null
  userDisplayName: string | null
  data: unknown
  timestamp: string
}

// Hubs/Broadcasters/AdminHubStatusPublisher.cs: the anonymous { System, Stats } snapshot (IAdminClient.ReceiveSystemStatus)
export type AdminSystemStatusPayload = {
  system: unknown
  stats: unknown
}
