// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { actionRequiredKey } from '@/api/actionRequired'
import { myChannelsQuery, type ChannelSummary } from '@/api/channels'
import { builtinsKey, commandsKey, type CommandListItem } from '@/api/commands'
import { shieldKey } from '@/api/shield'
import { streamHistoryKey, streamInfoKey, type StreamInfo } from '@/api/stream'
import { ttsConfigKey } from '@/api/tts'
import { widgetsKey } from '@/api/widgets'

import type { HubEventHandler } from './hubEvent'
import type {
  ChannelEventPayload,
  CommandExecutedPayload,
  ConfigChangedPayload,
  StreamInfoChangedPayload,
  StreamStatusPayload,
} from './payloads'

// The server names the domain of the action-required inbox "notifications" (ActionRequiredChangeNotifier.Domain).
const actionRequiredDomain = 'notifications'
// CommandService.cs and BuiltinCommandService.cs publish ConfigChanged with these domains.
const commandsDomain = 'commands'
const builtinsDomain = 'builtins'
// TtsConfigService.cs publishes ConfigChanged with this domain after a config change.
const ttsDomain = 'tts-config'
// WidgetService.cs publishes ConfigChanged with this domain when an overlay is created, updated or deleted.
const widgetsDomain = 'widgets'

function bare(name: string): string {
  const unprefixed = name.startsWith('!')
    ? name.slice(1)
    : name

  return unprefixed.toLowerCase()
}

// The payloads carry no ULID channel id, so each handler writes for the channel this connection joined.
export const channelHandlers: [
  HubEventHandler<StreamStatusPayload>,
  HubEventHandler<ChannelEventPayload>,
  HubEventHandler<ConfigChangedPayload>,
  HubEventHandler<CommandExecutedPayload>,
  HubEventHandler<StreamInfoChangedPayload>,
] = [
  {
    event: 'StreamStatusChanged',
    handle: ({ queries, channelId }, status) => {
      queries.setQueryData<ChannelSummary[]>(myChannelsQuery.queryKey, (rows) =>
        rows?.map((row) =>
          row.id === channelId
            ? { ...row, isLive: status.isLive }
            : row),
      )
      // Going offline carries no title or category, so the page keeps the ones it shows.
      queries.setQueryData<StreamInfo>(streamInfoKey(channelId), (info) =>
        info
          ? {
              ...info,
              isLive: status.isLive,
              startedAt: status.startedAt,
              title: status.title ?? info.title,
              gameName: status.gameName ?? info.gameName,
            }
          : info,
      )
      void queries.invalidateQueries({ queryKey: streamHistoryKey(channelId) })
    },
  },
  {
    event: 'ChannelEvent',
    handle: ({ queries, channelId }, event) => {
      if (event.type === 'shield_mode_begin') {
        queries.setQueryData<boolean>(shieldKey(channelId), true)
      }

      if (event.type === 'shield_mode_end') {
        queries.setQueryData<boolean>(shieldKey(channelId), false)
      }
    },
  },
  {
    event: 'ConfigChanged',
    handle: ({ queries, channelId }, change) => {
      if (change.domain === actionRequiredDomain) {
        void queries.invalidateQueries({ queryKey: actionRequiredKey(channelId) })
      }

      if (change.domain === commandsDomain) {
        void queries.invalidateQueries({ queryKey: commandsKey(channelId) })
      }

      if (change.domain === builtinsDomain) {
        void queries.invalidateQueries({ queryKey: builtinsKey(channelId) })
      }

      if (change.domain === ttsDomain) {
        void queries.invalidateQueries({ queryKey: ttsConfigKey(channelId) })
      }

      if (change.domain === widgetsDomain) {
        void queries.invalidateQueries({ queryKey: widgetsKey(channelId) })
      }
    },
  },
  {
    event: 'CommandExecuted',
    handle: ({ queries, channelId }, run) => {
      if (!run.succeeded) return

      queries.setQueryData<CommandListItem[]>(commandsKey(channelId), (rows) =>
        rows?.map((row) =>
          bare(row.name) === bare(run.commandName)
            ? { ...row, useCount: Number(row.useCount) + 1 }
            : row,
        ),
      )
    },
  },
  {
    event: 'StreamInfoChanged',
    handle: ({ queries, channelId }, change) => {
      queries.setQueryData<StreamInfo>(streamInfoKey(channelId), (info) =>
        info
          ? { ...info, title: change.title, gameName: change.gameName }
          : info,
      )
    },
  },
]
