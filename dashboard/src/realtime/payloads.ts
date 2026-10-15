// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { JsonValue } from '@/lib/json';
import type { components } from '@/api/schema';

// The hubs are not in v1.json, so their payloads are written here, once, from the server records.
// Wire names are camelCase. Source: server/src/NomNomzBot.Api/Hubs/Dtos/*.cs, Hubs/Clients/*.cs.

// HubResponseDtos.cs: StreamStatusDto
// The title or category changed on Twitch (EventSub channel.update).
export type StreamInfoChangedPayload = {
  broadcasterId: string;
  broadcasterDisplayName: string;
  title: string;
  gameName: string;
};

export type StreamStatusPayload = {
  isLive: boolean;
  streamId: string | null;
  title: string | null;
  gameName: string | null;
  startedAt: string | null;
};

// HubResponseDtos.cs: ModActionDto
export type ModActionPayload = {
  action: string;
  moderatorId: string;
  targetUserId: string;
  targetDisplayName?: string | null;
  reason: string | null;
  durationSeconds: number | null;
  broadcasterId: string;
};

// HubResponseDtos.cs: ConfigChangedDto
export type ConfigChangedPayload = {
  broadcasterId: string;
  domain: string;
  entityId: string | null;
  action: string;
};

// HubResponseDtos.cs: CommandExecutedDto. Whether commandName carries the leading "!": not checked, so readers accept both.
export type CommandExecutedPayload = {
  broadcasterId: string;
  commandName: string;
  triggeredByUserId: string;
  succeeded: boolean;
  timestamp: string;
};

// AlertDtos.cs: ChannelEventDto. The type is the method key the server gives the event.
export type ChannelEventPayload = {
  type: string;
  broadcasterId: string;
  userId: string | null;
  userDisplayName: string | null;
  data: JsonValue;
  timestamp: string;
};

// Hubs/Broadcasters/AdminHubStatusPublisher.cs: the anonymous { System, Stats } snapshot (IAdminClient.ReceiveSystemStatus)
export type AdminSystemStatusPayload = {
  system: components['schemas']['AdminSystemDto'];
  stats: components['schemas']['AdminStatsDto'];
};

// Hubs/Clients/IAdminClient.cs: ReceiveChannelRegistryUpdate (AdminBroadcastHandlers.cs). Three anonymous shapes share one event:
// going live carries isLive, streamTitle and gameName; going offline carries isLive; a suspension carries status.
export type AdminChannelRegistryPayload = {
  broadcasterId: string;
  channelName?: string;
  isLive?: boolean;
  streamTitle?: string | null;
  gameName?: string | null;
  status?: string;
};

// Hubs/Clients/IDashboardClient.cs: ChatMessage carries DashboardChatMessageDto, the same record the history call returns.
export type ChatMessagePayload = components['schemas']['DashboardChatMessageDto'];

// ChatModerationBroadcastHandlers.cs: the data of the ChannelEvent with type "message_deleted".
export type MessageDeletedPayload = {
  messageId: string;
  deletedByUserId: string;
  targetUserId: string;
  deletedByDisplayName: string | null;
};

// ChannelEvent type "chat_cleared".
export type ChatClearedPayload = {
  clearedByUserId: string;
};

// ChannelEvent type "user_messages_cleared". It belongs to the activity class, not the moderation class.
export type UserMessagesClearedPayload = {
  targetUserId: string;
  targetUserDisplayName: string | null;
  targetUserLogin: string | null;
};

// Result of JoinChannel and JoinChannelClasses (HubResponseDtos.cs: JoinChannelResponse).
export type JoinChannelResult = {
  success: boolean;
  error: string | null;
  streamStatus: StreamStatusPayload | null;
  grantedClasses: string[] | null;
};

// HubResponseDtos.cs: MusicTrackDto. Progress is an anchor: progressMs at observedAt.
export type MusicTrackPayload = {
  trackName: string;
  artist: string;
  album: string;
  albumArtUrl: string | null;
  durationMs: number;
  provider: string;
  progressMs: number;
  observedAt: string;
};

// HubResponseDtos.cs: MusicStateDto
export type MusicStatePayload = {
  isPlaying: boolean;
  currentTrack: MusicTrackPayload | null;
};

// WidgetEventPayloads.cs: SrQueueWidgetPayload, sent under the method sr_queue_changed (SrQueueBroadcastHandler.cs).
export type SrQueueChangedPayload = {
  items: {
    title: string;
    requestedBy: string;
    durationSec: number;
    code: string;
  }[];
};

// HubResponseDtos.cs: RewardRedeemedDto
export type RewardRedeemedPayload = {
  broadcasterId: string;
  rewardId: string;
  rewardTitle: string;
  redemptionId: string;
  userId: string;
  userDisplayName: string;
  cost: number;
  userInput: string | null;
  timestamp: string;
  avatarUrl?: string | null;
  pronouns?: string | null;
  communityStanding?: string | null;
  eventId?: string | null;
};

// HubResponseDtos.cs: RedemptionStatusChangedDto
export type RedemptionStatusChangedPayload = {
  broadcasterId: string;
  redemptionId: string;
  rewardId: string;
  status: string;
  timestamp: string;
};

// HubResponseDtos.cs: RewardChangedDto
export type RewardChangedPayload = {
  broadcasterId: string;
  action: string;
  rewardId: string;
  title: string;
  cost: number | null;
  isEnabled: boolean | null;
  timestamp: string;
};

// AlertDtos.cs: AlertDto. The data differs per alert type, so a reader narrows it by type.
export type AlertTriggeredPayload = {
  type: string;
  message: string | null;
  data: JsonValue;
};

// HubResponseDtos.cs: PermissionChangedDto
export type PermissionChangedPayload = {
  subjectType: string;
  subjectId: string;
  resourceType: string;
  resourceId: string;
  value: number;
};

// HubResponseDtos.cs: ObsBridgeStateDto
export type ObsBridgeStatePayload = {
  broadcasterId: string;
  instanceCount: number;
  hasLeader: boolean;
  timestamp: string;
};

// HubResponseDtos.cs: ObsLiveStateDto
export type ObsLiveStatePayload = {
  broadcasterId: string;
  streaming: boolean;
  recording: boolean;
  timestamp: string;
};

// Every event the dashboard hub sends to a joined channel (Hubs/Clients/IDashboardClient.cs, plus the sr_queue_changed
// method the notifier sends by name), keyed by the method name. A handler is typed from this map by its event name.
export type DashboardHubEvents = {
  ChatMessage: ChatMessagePayload;
  ChannelEvent: ChannelEventPayload;
  PermissionChanged: PermissionChangedPayload;
  MusicStateChanged: MusicStatePayload;
  ModAction: ModActionPayload;
  CommandExecuted: CommandExecutedPayload;
  RewardRedeemed: RewardRedeemedPayload;
  RedemptionStatusChanged: RedemptionStatusChangedPayload;
  StreamStatusChanged: StreamStatusPayload;
  AlertTriggered: AlertTriggeredPayload;
  StreamInfoChanged: StreamInfoChangedPayload;
  RewardChanged: RewardChangedPayload;
  ConfigChanged: ConfigChangedPayload;
  ObsBridgeStateChanged: ObsBridgeStatePayload;
  ObsLiveStateChanged: ObsLiveStatePayload;
  sr_queue_changed: SrQueueChangedPayload;
};
