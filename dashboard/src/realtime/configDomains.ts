// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { actionRequiredKey } from '@/api/actionRequired';
import { builtinsKey, commandsKey } from '@/api/commands';
import { botStatusKey, connectionsKey, reviewQueueKey } from '@/api/home';
import { timersKey } from '@/api/liveOps';
import { musicConfigKey } from '@/api/music';
import { communityListsKey } from '@/api/people';
import { pipelinesKey } from '@/api/pipelines';
import { ttsConfigKey } from '@/api/tts';
import { widgetsKey } from '@/api/widgets';

import type { QueryKeyBuilder } from './hubEvent';

// The key of api/eventResponses.ts. It is built here so this table does not depend on that module.
const eventResponsesKey: QueryKeyBuilder = (channelId) => ['channels', channelId, 'event-responses'];

// The domain names are the ones the server services publish with ConfigChanged
// (ActionRequiredChangeNotifier, CommandService, BuiltinCommandService, TtsConfigService, WidgetService, and the
// ChannelConfigChangedEvent publishers). Each domain lists the page queries its change makes stale.
export const configDomainKeys: Record<string, readonly QueryKeyBuilder[]> = {
  notifications: [actionRequiredKey],
  commands: [commandsKey],
  builtins: [builtinsKey],
  'tts-config': [ttsConfigKey],
  widgets: [widgetsKey],
  timers: [timersKey],
  'event-responses': [eventResponsesKey],
  pipelines: [pipelinesKey],
  'music-config': [musicConfigKey],
  'sr-config': [musicConfigKey],
  'channel-settings': [botStatusKey, connectionsKey],
  features: [botStatusKey],
  webhooks: [connectionsKey],
  'moderation-history': [reviewQueueKey, communityListsKey],
  'suspicious-users': [reviewQueueKey, communityListsKey],
  'unban-requests': [reviewQueueKey, communityListsKey],
  'viewer-reports': [reviewQueueKey, communityListsKey],
  'raid-assessments': [reviewQueueKey, communityListsKey],
};

// A domain with no page key of its own (catalog, earning-rules, economy-config, quotes, savings-jar, picklists, or a
// new one) marks everything the channel owns as stale: every query of a channel starts with one of these three.
export const channelPrefixes: readonly QueryKeyBuilder[] = [
  (channelId) => ['channels', channelId],
  (channelId) => ['community', channelId],
  (channelId) => ['dashboard', channelId],
];
