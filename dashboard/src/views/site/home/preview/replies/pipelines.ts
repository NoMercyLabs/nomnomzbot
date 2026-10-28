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

import { lookup, merge } from '@/lib/array';

import type { FakeReply, Json } from '../fakeApi';
import optionsTwitchUser from '../fixtures/options-twitch-user.json';
import optionsVoice from '../fixtures/options-voice.json';
import pipelineActions from '../fixtures/pipeline-actions.json';
import pipelineDetails from '../fixtures/pipeline-details.json';
import pipelinesCapture from '../fixtures/pipelines.json';
import { channelPath, fixture, get, list, minutesAgo, one, sampleChannelId } from './shape';

// The five pipelines a fresh install seeds, with their real steps, plus one branching pipeline the sample channel
// built itself (a fresh install has no `if` block in any seeded pipeline). The seeded steps are the server's own.
const seeded = fixture(pipelineDetails);

type Seeded = (typeof seeded)[number];

function seededByName(name: string): Seeded {
  return seeded.find((entry) => entry.pipeline.name === name) as Seeded;
}

export const seededPipelineNames = seeded.map((entry) => entry.pipeline.name);

// The id of a seeded pipeline by its name, and the id of a step of it by its action: for the preview paths.
export function pipelineIdOf(name: string): string {
  return seededByName(name).pipeline.id;
}

export function stepIdOf(name: string, actionType: string): string {
  const steps = seededByName(name).pipeline.graph.steps as {
    id: string;
    action: { type: string };
  }[];

  return steps.find((step) => step.action.type === actionType)?.id ?? '';
}

export const branchingPipelineId = 'sample-big-raid-welcome';

type Step = {
  id: string;
  parent_step_id: string | null;
  branch: string | null;
  block_kind: string | null;
  block_config: null;
  order: number;
  action: Record<string, Json>;
  condition: Record<string, Json> | null;
  continue_on_error: boolean;
  stop_on_match: boolean;
};

function step(over: Partial<Step> & Pick<Step, 'id' | 'action' | 'order'>): Step {
  return {
    parent_step_id: null,
    branch: null,
    block_kind: null,
    block_config: null,
    condition: null,
    continue_on_error: false,
    stop_on_match: false,
    ...over,
  };
}

// A big raid gets a shoutout and a welcome; a small one gets a thank you. The condition is the server's own
// comparison condition, and the template variables are helpers the server lists for event responses.
const branchingSteps: Step[] = [
  step({
    id: 'big-raid-check',
    block_kind: 'if',
    order: 0,
    action: { type: 'if' },
    condition: {
      type: 'comparison',
      left: '{viewers}',
      operator: 'gte',
      right: '50',
      negate: false,
    },
  }),
  step({
    id: 'big-raid-shoutout',
    parent_step_id: 'big-raid-check',
    branch: 'then',
    order: 0,
    action: {
      type: 'shoutout',
      user_id: '{user.id}',
    },
  }),
  step({
    id: 'big-raid-welcome',
    parent_step_id: 'big-raid-check',
    branch: 'then',
    order: 1,
    action: {
      type: 'send_message',
      message: 'Welcome in, {user.name}! {viewers} raiders just landed.',
    },
  }),
  step({
    id: 'big-raid-thanks',
    parent_step_id: 'big-raid-check',
    branch: 'else',
    order: 0,
    action: {
      type: 'send_message',
      message: 'Thanks for the raid, {user.name}!',
    },
  }),
  step({
    id: 'big-raid-recap',
    order: 1,
    action: { type: 'show_raid_recap' },
  }),
];

const branching = {
  id: branchingPipelineId,
  channelId: sampleChannelId,
  name: 'Big raid welcome',
  description: 'Greets every raid, and goes further for a big one.',
  isEnabled: true,
  triggerKind: 'event',
  graph: { steps: branchingSteps },
  triggerCount: 14,
  createdAt: minutesAgo(60 * 24 * 9),
  updatedAt: minutesAgo(60 * 5),
};

// How often the sample channel's pipelines ran: sample numbers on top of the seeded records.
const sampleTriggerCounts = {
  'Raid response': 9,
  'Raid out': 3,
  'Raid committed': 3,
  'Raid starting': 3,
  'Cheer response': 27,
} as const satisfies Record<string, number>;

function triggerCountOf(name: string): number {
  return lookup(sampleTriggerCounts, name) ?? 0;
}

function lastRunOf(name: string): string | null {
  return triggerCountOf(name) > 0
    ? minutesAgo(190)
    : null;
}

const listed = fixture(pipelinesCapture).data.map((pipeline) => ({
  ...pipeline,
  triggerCount: triggerCountOf(pipeline.name),
  lastTriggeredAt: lastRunOf(pipeline.name),
  parameterNames: [],
}));

const listedAll = merge(
  listed,
  [
    {
      id: branching.id,
      name: branching.name,
      description: branching.description,
      isEnabled: true,
      triggerCount: branching.triggerCount,
      lastTriggeredAt: minutesAgo(190),
      updatedAt: branching.updatedAt,
      parameterNames: [],
      hasPlatformDefault: false,
    },
  ],
);

const details = seeded.map((entry) => ({
  ...entry.pipeline,
  triggerCount: triggerCountOf(entry.pipeline.name),
  lastTriggeredAt: lastRunOf(entry.pipeline.name),
}));

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

// Runs of a pipeline: sample history, in the field set the server sends for a run.
const runPlan = [
  {
    ago: 190,
    ms: 640,
    calls: 3,
  },
  {
    ago: 60 * 26,
    ms: 580,
    calls: 3,
  },
  {
    ago: 60 * 52,
    ms: 710,
    calls: 3,
  },
  {
    ago: 60 * 24 * 4,
    ms: 530,
    calls: 3,
  },
  {
    ago: 60 * 24 * 6,
    ms: 690,
    calls: 3,
  },
] as const;

function runsOf(pipelineId: string, firstRunId: number) {
  return runPlan.map((run, index) => ({
    id: firstRunId + index,
    pipelineId,
    triggerKind: 'event',
    status: 'completed',
    hostCallCount: run.calls,
    durationMs: run.ms,
    errorMessage: null,
    startedAt: minutesAgo(run.ago),
    completedAt: minutesAgo(run.ago - 1),
  }));
}

const raidResponseId = pipelineIdOf('Raid response');
const allRuns = merge(runsOf(raidResponseId, 100), runsOf(branchingPipelineId, 200));

function runsFor(url: URL) {
  const wanted = url.searchParams.get('pipelineId');

  const matching = allRuns.filter((run) => wanted === null || run.pipelineId === wanted);

  return list(matching);
}

const fixedReplies = [
  get('/api/v1/pipelines/options/voice', fixture(optionsVoice)),
  get('/api/v1/pipelines/options/twitch_user', fixture(optionsTwitchUser)),
  get(`${channelPath}/pipelines`, list(listedAll)),
  get(`${channelPath}/pipelines/actions`, fixture(pipelineActions)),
  get(`${channelPath}/pipelines/${branchingPipelineId}`, one(branching)),
  get(`${channelPath}/pipelines/${branchingPipelineId}/blast-radius`, one(radius)),
  get(`${channelPath}/pipeline-executions`, (url) =>
    runsFor(url)),
];

const detailReplies = details.map((detail) =>
  get(`${channelPath}/pipelines/${detail.id}`, one(detail)));

const radiusReplies = seeded.map((entry) =>
  get(`${channelPath}/pipelines/${entry.pipeline.id}/blast-radius`, one(entry.blastRadius)));

export const pipelineReplies: readonly FakeReply[] = merge(
  fixedReplies,
  detailReplies,
  radiusReplies,
);
