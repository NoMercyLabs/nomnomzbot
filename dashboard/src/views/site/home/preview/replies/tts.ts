// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { FakeReply } from '../fakeApi';
import voiceOptions from '../fixtures/options-voice.json';
import { channelPath, get, list, minutesAgo, one } from './shape';

// A new channel's text-to-speech settings: the defaults of the server's TtsConfig entity and TtsConfigOptions.
const config = {
  isEnabled: true,
  mode: 'self_host',
  defaultProvider: 'edge',
  defaultVoiceId: null,
  maxCharacters: 500,
  minPermission: 'everyone',
  skipBotMessages: true,
  readUsernames: true,
  profanityCensorEnabled: true,
  modApprovalRequired: false,
  minBitsToTts: null,
  viewerVoiceSelfServiceEnabled: true,
  hasAzureByokKey: false,
  hasElevenLabsByokKey: false,
  azureRegion: null,
  followsPlatformDefaultVoice: true,
  cooldownSeconds: 0,
} as const;

// The voices the server lists (the voice option list of a fresh install), read into the voice shape of the page.
// Each item holds its id, its label, and "locale · gender · provider" as its second line.
const voices = voiceOptions.data.items.map((item) => {
  const [locale = '', gender = '', provider = ''] = item.secondaryText.split(' · ');

  return {
    id: item.value,
    name: item.value,
    displayName: item.label,
    locale,
    gender: gender.toLowerCase(),
    provider,
    isDefault: false,
    accent: null,
    age: null,
    styles: [],
    tags: [],
    description: null,
    previewUrl: null,
  };
});

// Invented viewers and lines; the voices are real ones from the list above.
const spoken = [
  {
    by: 'daisy_plays',
    text: 'good luck on the boss, you got this!',
    voice: 0,
  },
  {
    by: 'nite_owl22',
    text: 'that clutch deserves a clip',
    voice: 1,
  },
  {
    by: 'viewer_87',
    text: 'first time here, love the setup',
    voice: 2,
  },
  {
    by: 'chat_fan99',
    text: 'hydrate, chat, hydrate',
    voice: 3,
  },
] as const;

const englishVoices = voices.filter((voice) => voice.locale === 'en-US');

const history = spoken.map((line, index) => ({
  id: index + 1,
  text: line.text,
  voiceId: englishVoices[line.voice]?.id ?? '',
  provider: 'edge',
  requestedByUserId: `sample-viewer-${index}`,
  occurredAt: minutesAgo(20 + index * 35),
  requestedByName: line.by,
  requestedByColor: null,
}));

const tts = `${channelPath}/tts`;

const voicePage = {
  ...list(voices),
  nextPage: null,
};

const historyPage = one({
  items: history,
  nextBefore: null,
});

export const ttsReplies: FakeReply[] = [
  get(`${tts}/config`, one(config)),
  get(`${tts}/voices`, voicePage),
  get(`${tts}/history`, historyPage),
];
