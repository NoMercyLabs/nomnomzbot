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

// The payload a new definition starts from, one per kind: the blank form of the channel's own editor, written as the
// JSON its payload form reads. A required field left blank here blocks Save in that form, not the reader.
import type { ContentKind } from '@/api/adminContent';
import { defaultMinutes } from '@/views/dashboard/timers/components/timerDraft';

const starters = {
  pipeline: { steps: [] },
  code_script: { sourceCode: '' },
  widget: {
    sourceCode: '',
    defaultSettings: {},
    defaultEventSubscriptions: [],
  },
  command: {},
  event_response: {
    eventType: 'channel.follow',
    responseType: 'chat_message',
    isEnabled: true,
  },
  timer: {
    name: '',
    messages: [],
    intervalMinutes: defaultMinutes,
    minChatActivity: 0,
    fireOnce: false,
    isEnabled: true,
  },
  reward: {
    isUserInputRequired: false,
    speaksViaTts: false,
  },
  pick_list: {
    name: '',
    description: '',
    items: [],
  },
  sound_clip: {
    name: '',
    displayName: '',
    assetId: '',
    defaultVolume: 80,
    cooldownSeconds: 0,
    minPermissionLevel: 'Everyone',
  },
} as const satisfies Record<ContentKind, object>;

// The starter of a kind as JSON; a kind with no form starts empty.
export function starterPayload(kind: string): string {
  const starter = (starters as Record<string, object>)[kind] ?? {};

  return JSON.stringify(starter);
}
