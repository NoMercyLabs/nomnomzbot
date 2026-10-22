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
import { channelPath, get, list, minutesAgo, one, sampleChannelId } from './shape';

export const raidPipelineId = 'sample-raid-welcome';

type Step = {
  id: string;
  parent_step_id?: string;
  branch?: string;
  block_kind?: string;
  order: number;
  action: Record<string, Json>;
  condition?: Record<string, Json>;
};

// A branching flow: a big raid gets a shoutout read out loud and a welcome; a small one gets a thank you.
const steps: Step[] = [
  {
    id: 'check',
    block_kind: 'if',
    order: 0,
    action: { type: 'if' },
    condition: {
      type: 'comparison',
      operator: 'gte',
      left: '{viewers}',
      right: '50',
      negate: false,
    },
  },
  {
    id: 'shout',
    parent_step_id: 'check',
    branch: 'then',
    order: 0,
    action: {
      type: 'shoutout',
      user_id: '{user.id}',
      tts: true,
    },
  },
  {
    id: 'welcome',
    parent_step_id: 'check',
    branch: 'then',
    order: 1,
    action: {
      type: 'send_message',
      message: 'Welcome in, {user.name}! {viewers} raiders just landed.',
    },
  },
  {
    id: 'thanks',
    parent_step_id: 'check',
    branch: 'else',
    order: 0,
    action: { type: 'send_message', message: 'Thanks for the raid, {user.name}!' },
  },
  {
    id: 'pause',
    order: 1,
    action: { type: 'wait', seconds: 3 },
  },
  {
    id: 'recap',
    order: 2,
    action: { type: 'show_raid_recap' },
  },
];

const pipeline = {
  id: raidPipelineId,
  channelId: sampleChannelId,
  name: 'Raid welcome',
  description: 'Greets every raid, and goes further for a big one.',
  isEnabled: true,
  triggerKind: 'event',
  graph: { steps },
  triggerCount: 42,
  lastTriggeredAt: minutesAgo(190),
  createdAt: minutesAgo(60 * 24 * 30),
  updatedAt: minutesAgo(60 * 5),
};

function field(name: string, kind: string, over: Record<string, Json> = {}) {
  return {
    name,
    kind,
    required: false,
    repeatable: false,
    description: null,
    templated: false,
    declaresVariable: false,
    ...over,
  };
}

function action(type: string, category: string, fields: Json[]) {
  return {
    type,
    category: { key: `pipeline.category.${category}` },
    description: { key: `pipeline.${type}.description` },
    fields,
  };
}

const catalog = {
  actions: [
    action('send_message', 'chat', [field('message', 'text', { required: true, templated: true })]),
    action('shoutout', 'chat', [
      field('user_id', 'text', { required: true, templated: true }),
      field('tts', 'boolean'),
    ]),
    action('wait', 'flow', [field('seconds', 'number', { required: true })]),
    action('show_raid_recap', 'widgets', []),
    action('play_sound', 'sound', [field('sound', 'text', { required: true })]),
    action('set_variable', 'flow', [
      field('name', 'text', { required: true }),
      field('value', 'text', { templated: true }),
    ]),
  ],
  conditions: [{ type: 'comparison' }],
};

const radius = {
  commandCount: 0,
  commandNames: [],
  chatTriggerCount: 0,
  chatTriggerPatterns: [],
  timerCount: 0,
  timerNames: [],
  eventResponseCount: 1,
  eventResponseEventTypes: ['channel.raid'],
  totalReferences: 1,
};

export const pipelineReplies: FakeReply[] = [
  get(
    `${channelPath}/pipelines`,
    list([
      {
        id: pipeline.id,
        name: pipeline.name,
        description: pipeline.description,
        isEnabled: true,
        triggerCount: pipeline.triggerCount,
        lastTriggeredAt: pipeline.lastTriggeredAt,
        updatedAt: pipeline.updatedAt,
        parameterNames: [],
        hasPlatformDefault: true,
      },
    ]),
  ),
  get(`${channelPath}/pipelines/actions`, one(catalog)),
  get(`${channelPath}/pipelines/${raidPipelineId}`, one(pipeline)),
  get(`${channelPath}/pipelines/${raidPipelineId}/blast-radius`, one(radius)),
  get(`${channelPath}/pipeline-executions`, list([])),
];
