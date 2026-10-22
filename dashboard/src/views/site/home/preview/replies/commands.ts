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
import { channelPath, get, list, minutesAgo, one } from './shape';

type SampleCommand = {
  name: string;
  tier: 'template' | 'pipeline';
  response: string;
  uses: number;
  level?: string;
  cooldown?: number;
  aliases?: string[];
};

// Replies that show template variables, so the list reads as what the bot really fills in.
const commands: SampleCommand[] = [
  {
    name: 'uptime',
    tier: 'template',
    response: '{{channel.name}} has been live for {{stream.uptime}}.',
    uses: 311,
    aliases: ['up'],
  },
  {
    name: 'socials',
    tier: 'template',
    response: 'Hey {{user.name}}, follow on X and Kick: the links are in the panel.',
    uses: 187,
  },
  {
    name: 'so',
    tier: 'pipeline',
    response: '',
    uses: 96,
    level: 'moderator',
  },
  {
    name: 'hug',
    tier: 'template',
    response: '{{user.name}} hugs {{args.1}}. That is hug number {{counter.hugs}}.',
    uses: 842,
    cooldown: 10,
  },
  {
    name: 'followage',
    tier: 'template',
    response: '{{user.name}} has followed {{channel.name}} for {{user.followage}}.',
    uses: 402,
  },
];

function row(command: SampleCommand, index: number) {
  return {
    id: `sample-command-${index}`,
    name: command.name,
    tier: command.tier,
    minPermissionLevel: command.level ?? 'everyone',
    isEnabled: true,
    prefixMode: 'default',
    customPrefix: null,
    matchMode: 'exact',
    matchPattern: null,
    cooldownSeconds: command.cooldown ?? 5,
    userCooldownSeconds: 0,
    description: null,
    aliases: command.aliases ?? [],
    useCount: command.uses,
    createdAt: minutesAgo(60 * 24 * (20 + index)),
    templateResponse: command.response === ''
      ? null
      : command.response,
    templateResponses: null,
    pipelineId: null,
    presetKey: null,
    speakWithTts: false,
  };
}

const builtins = ['song', 'shoutout', 'quote', 'accountage', 'balance'].map((key) => ({
  builtinKey: key,
  name: key,
  isEnabled: true,
  defaultCooldownSeconds: 5,
  defaultUserCooldownSeconds: 0,
  defaultMinPermissionLevel: 'everyone',
  replyGroup: key,
  speakWithTts: false,
  isReserved: false,
  isAlwaysOn: false,
  replyOverrideCount: 0,
}));

const rows = commands.map(row);

// The variables a reply may use, with the words the dashboard has for each and an example for the sample channel.
const helperSource = [
  ['channel', 'channel', 'crumb_tv'],
  ['channel.display', 'channel_display', 'crumb_tv'],
  ['streamer', 'streamer', 'crumb_tv'],
  ['stream.title', 'stream_title', 'Ranked climb, day 9'],
  ['stream.game', 'stream_game', 'Valorant'],
  ['stream.uptime', 'stream_uptime', '2h 14m'],
  ['stream.viewers', 'stream_viewers', '1204'],
  ['stream.isLive', 'stream_is_live', 'true'],
  ['user', 'user', 'daisy_plays'],
  ['user.name', 'user_name', 'daisy_plays'],
  ['user.id', 'user_id', '1001'],
  ['viewers', 'viewers', '214'],
] as const satisfies readonly (readonly [string, string, string])[];

const helpers = helperSource.map(([key, words, sample]) => ({
  key,
  descriptionKey: `template.helper.${words}`,
  sample,
}));

export const commandReplies: FakeReply[] = [
  get(`${channelPath}/commands`, list(rows)),
  get(`${channelPath}/builtins`, one(builtins)),
  get(`${channelPath}/command-presets`, one([])),
  get('/api/v1/templates/helpers', one(helpers)),
];
