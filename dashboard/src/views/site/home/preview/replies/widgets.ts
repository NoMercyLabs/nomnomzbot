// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { FakeReply, Json } from '../fakeApi';
import { channelPath, get, list, minutesAgo, one } from './shape';

type SampleWidget = {
  id: string;
  name: string;
  description: string;
  events: string[];
  enabled?: boolean;
  attached?: boolean;
  customized?: boolean;
  settings?: Record<string, Json>;
};

const widgets: SampleWidget[] = [
  {
    id: 'sample-widget-alerts',
    name: 'Alerts',
    description: 'Follows, subscriptions, gifts and cheers pop up with a sound and a line of text.',
    events: ['follow', 'subscription', 'resub', 'gift', 'cheer'],
    customized: true,
    settings: {
      duration: 6,
      volume: 70,
      showAvatar: true,
    },
  },
  {
    id: 'sample-widget-chat',
    name: 'Chat box',
    description: 'Chat from every connected platform in one themed box, with emotes and badges.',
    events: ['ChatMessage', 'ChatMessageEnriched'],
    settings: { fontSize: 22, fadeSeconds: 40 },
  },
  {
    id: 'sample-widget-nowplaying',
    name: 'Now playing',
    description: 'The current track with cover art and the name of the viewer who asked for it.',
    events: ['now_playing', 'track_saved_changed'],
  },
  {
    id: 'sample-widget-queue',
    name: 'Song queue',
    description: 'The next requested tracks, in order.',
    events: ['sr_queue'],
    attached: false,
  },
  {
    id: 'sample-widget-hype',
    name: 'Hype train',
    description: 'Level, progress and the top contributors while a hype train runs.',
    events: ['hype_train_begin', 'hype_train_progress', 'hype_train_end'],
  },
  {
    id: 'sample-widget-poll',
    name: 'Polls and predictions',
    description: 'Live votes and outcomes of Twitch polls and predictions.',
    events: ['poll_begin', 'poll_progress', 'poll_end', 'prediction_begin', 'prediction_end'],
    enabled: false,
  },
  {
    id: 'sample-widget-leaderboard',
    name: 'Leaderboard',
    description: 'The top viewers by currency, shown on demand from a command.',
    events: ['leaderboard.show'],
  },
  {
    id: 'sample-widget-tts',
    name: 'Voice lines',
    description: 'Speaks chat, reward text and bot lines in the voice you pick.',
    events: ['tts_speak'],
  },
];

function widget(entry: SampleWidget, index: number) {
  return {
    id: entry.id,
    name: entry.name,
    description: entry.description,
    framework: 'vue',
    source: 'first_party',
    isEnabled: entry.enabled ?? true,
    overlayUrl: `https://overlay.example.test/${entry.id}`,
    activeVersionId: `${entry.id}-v1`,
    galleryItemId: null,
    settings: entry.settings ?? {},
    eventSubscriptions: entry.events,
    lastRuntimeError: null,
    lastRanAt: minutesAgo(3 + index),
    createdAt: minutesAgo(60 * 24 * 60),
    updatedAt: minutesAgo(60 * 24 * (2 + index)),
    galleryUpdateAvailable: false,
    isAttached: entry.attached ?? true,
    isCustomized: entry.customized ?? false,
    obsSetup: {
      width: 1920,
      height: 1080,
      controlAudioViaObs: false,
      shutdownWhenHidden: false,
    },
  };
}

const all = widgets.map(widget);

// The shared surfaces every channel gets; the Overlays page lists the rest as the channel's own.
const required = all.slice(0, 2).map((widget, index) => ({
  key: index === 0
    ? 'alerts'
    : 'chat',
  widget,
}));

export const widgetReplies: FakeReply[] = [
  get(`${channelPath}/widgets`, list(all)),
  get(`${channelPath}/overlays/required`, one(required)),
  get(`${channelPath}/widgets/*/settings-schema`, one({ fields: [] })),
];
