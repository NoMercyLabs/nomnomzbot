// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { lookup, merge } from '@/lib/array';

import type { FakeReply } from '../fakeApi';
import builtins from '../fixtures/builtins.json';
import commandPresets from '../fixtures/command-presets.json';
import commandsCapture from '../fixtures/commands.json';
import helpersCommand from '../fixtures/helpers-command.json';
import helpersEvent from '../fixtures/helpers-event.json';
import helpersTimer from '../fixtures/helpers-timer.json';
import { channelPath, fixture, get, list, minutesAgo } from './shape';

type CustomCommand = {
  name: string;
  description: string;
  response: string;
  uses: number;
  cooldown: number;
};

// What a streamer adds on top of a fresh install. A fresh install holds none, so these are sample commands: their
// names are free (no built-in or preset uses them) and their replies use only helpers the server lists.
export const sampleCustomCommands = [
  {
    name: 'welcome',
    description: 'Greet a viewer and say what is on.',
    response: 'Welcome in, {user.name}! {channel} is playing {stream.game}.',
    uses: 146,
    cooldown: 10,
  },
  {
    name: 'title',
    description: 'Say what the stream is about.',
    response: '{channel} is live: {stream.title}',
    uses: 88,
    cooldown: 30,
  },
  {
    name: 'luck',
    description: 'Roll a number for a viewer.',
    response: '{user.name} rolled {random.number.100} out of 100.',
    uses: 311,
    cooldown: 5,
  },
] as const satisfies readonly CustomCommand[];

// The uses of the commands a fresh install seeds are zero; the sample shows a channel that has used them for weeks.
const sampleUses = {
  hug: 842,
  slap: 205,
  '8ball': 64,
  ping: 31,
  rps: 120,
  compliment: 52,
  raid: 9,
} as const satisfies Record<string, number>;

function usesOf(name: string): number {
  return lookup(sampleUses, name) ?? 0;
}

const captured = fixture(commandsCapture);

const seeded = captured.data.map((command) => ({
  ...command,
  useCount: usesOf(command.name),
}));

const custom = sampleCustomCommands.map((command, index) => ({
  id: `sample-command-${index}`,
  name: command.name,
  tier: 'template',
  minPermissionLevel: 'Everyone',
  isEnabled: true,
  prefixMode: 'Default',
  matchMode: 'StartsWith',
  cooldownSeconds: command.cooldown,
  userCooldownSeconds: 0,
  description: command.description,
  aliases: [],
  useCount: command.uses,
  createdAt: minutesAgo(60 * 24 * (12 - index * 3)),
  templateResponse: command.response,
  templateResponses: [],
  presetKey: null,
  speakWithTts: false,
}));

// Which of the server's three helper lists a page asks for. The pipeline editor asks with the event that starts
// the pipeline (a raid, a follow), so it gets the list of event placeholders.
const helperLists = {
  command: helpersCommand,
  eventResponse: helpersEvent,
  pipeline: helpersEvent,
  timer: helpersTimer,
} as const;

function helpersFor(url: URL) {
  const context = url.searchParams.get('context') ?? 'command';

  return lookup(helperLists, context) ?? helpersCommand;
}

const allCommands = merge(seeded, custom);

export const commandReplies: FakeReply[] = [
  get(`${channelPath}/commands`, list(allCommands)),
  get(`${channelPath}/builtins`, fixture(builtins)),
  get(`${channelPath}/command-presets`, fixture(commandPresets)),
  get('/api/v1/templates/helpers', (url) =>
    helpersFor(url)),
];
