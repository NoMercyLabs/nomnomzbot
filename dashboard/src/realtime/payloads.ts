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
  system: components['schemas']['AdminSystem'];
  stats: components['schemas']['AdminStats'];
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

// Hubs/Clients/IAdminClient.cs: ReceiveTrustSafetyChanged (AdminTrustSafetyBroadcastHandler). An operator confirmed or
// overturned a review, or applied or lifted a network block; area is review or network-block.
export type AdminTrustSafetyPayload = {
  area: string;
  action: string;
  subjectId: string;
};

// Hubs/Clients/IAdminClient.cs: ReceiveErrorBudgetChanged (AdminErrorBudgetBroadcastHandler). One outbound webhook
// attempt resolved for this tenant; status is Delivered, Failed or DeadLetter.
export type AdminErrorBudgetPayload = {
  broadcasterId: string;
  status: string;
};

// Hubs/Clients/IAdminClient.cs: ReceiveAuditChanged (AdminAuditBroadcastHandler). One platform permission was
// checked and an audit row written; outcome is Allowed or Denied.
export type AdminAuditPayload = {
  principalId: string;
  permission: string;
  outcome: string;
  targetBroadcasterId: string | null;
};

// Hubs/Clients/IAdminClient.cs: ReceivePlatformBotChanged (AdminPlatformBotBroadcastHandler). The shared platform
// bot connected (botUsername set) or disconnected.
export type AdminPlatformBotPayload = {
  connected: boolean;
  botUsername: string | null;
};

// Hubs/Clients/IAdminClient.cs: ReceiveContentChanged (AdminContentBroadcastHandler). An operator created a
// definition, drafted or published a version, or retired a definition.
export type AdminContentPayload = {
  action: string;
  definitionId: string;
};

// Hubs/Clients/IAdminClient.cs: ReceiveAccessChanged (AdminAccessBroadcastHandler). An operator provisioned a
// principal, assigned or revoked a role, or deactivated or reactivated a principal. A revoke names no principal.
export type AdminAccessPayload = {
  action: string;
  principalId: string | null;
  assignmentId: string | null;
};

// Hubs/Clients/IAdminClient.cs: ReceiveBillingChanged (AdminBillingBroadcastHandler). An operator changed a tier, an
// invite code, a grant, an invoice or a priced unit; area names which, targetBroadcasterId the channel it touched.
export type AdminBillingPayload = {
  area: string;
  action: string;
  subjectId: string;
  targetBroadcasterId?: string | null;
};

// Hubs/Clients/IDashboardClient.cs: ChatMessage carries DashboardChatMessageDto, the same record the history call returns.
export type ChatMessagePayload = components['schemas']['DashboardChatMessage'];

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
    number: number;
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

// HubResponseDtos.cs: RedemptionActivityDto. An automatic reward (title is Twitch's reward type, channel points)
// a custom Power-up (title is its name, Bits) or a Twitch Power-up such as a celebration (title is its type, Bits).
export type RedemptionActivityPayload = {
  broadcasterId: string;
  kind: 'automatic_reward' | 'custom_power_up' | 'power_up';
  redemptionId: string;
  title: string;
  userId: string;
  userLogin: string;
  userDisplayName: string;
  cost: number;
  costUnit: 'channel_points' | 'bits';
  userInput: string | null;
  timestamp: string;
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

// HubResponseDtos.cs: ObsStateChangedDto. Only the fields of the kind are set.
export type ObsStateChangedPayload = {
  broadcasterId: string;
  kind:
    | 'programScene' |
    'previewScene' |
    'stream' |
    'record' |
    'replayBuffer' |
// HubResponseDtos.cs: CustomDataSourceChangedDto. Change names what happened: created, updated, deleted or received.
export type CustomDataSourceChangedPayload = {
  sourceName: string;
  change: string;
};

// HubResponseDtos.cs: VtsStatusDto
export type VtsStatusPayload = {
  broadcasterId: string;
  status: string;
  timestamp: string;
};

    'virtualCam' |
    'studioMode' |
    'inputMute' |
    'inputVolume' |
    'sceneList' |
    'sceneItemVisibility' |
    'sceneItemLock' |
    'sourceFilter';
  sceneName?: string | null;
  active?: boolean | null;
  paused?: boolean | null;
  inputName?: string | null;
  muted?: boolean | null;
  volumeDb?: number | null;
  sceneItemId?: number | null;
  enabled?: boolean | null;
  locked?: boolean | null;
  sourceName?: string | null;
  filterName?: string | null;
  sceneNames?: string[] | null;
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
  RedemptionActivity: RedemptionActivityPayload;
  RedemptionStatusChanged: RedemptionStatusChangedPayload;
  StreamStatusChanged: StreamStatusPayload;
  AlertTriggered: AlertTriggeredPayload;
  StreamInfoChanged: StreamInfoChangedPayload;
  RewardChanged: RewardChangedPayload;
  ConfigChanged: ConfigChangedPayload;
  ObsBridgeStateChanged: ObsBridgeStatePayload;
  ObsLiveStateChanged: ObsLiveStatePayload;
  ObsStateChanged: ObsStateChangedPayload;
  sr_queue_changed: SrQueueChangedPayload;
};
  VtsStatusChanged: VtsStatusPayload;
  CustomDataSourceChanged: CustomDataSourceChangedPayload;
