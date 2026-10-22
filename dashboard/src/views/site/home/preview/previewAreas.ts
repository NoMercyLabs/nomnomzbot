// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { branchingPipelineId, pipelineIdOf, stepIdOf } from './replies/pipelines';
import { widgetIdOf } from './replies/widgets';

const raidPipelineId = pipelineIdOf('Raid response');

// The product areas the home page shows, each as a real dashboard page. The hero has a tab for every area;
// each feature group below it shows the same area again, on another view of the page and without the sidebar.
export const previewAreas = [
  'pipelines',
  'moderation',
  'widgets',
  'analytics',
  'commands',
  'loyalty',
  'music',
  'platforms',
] as const;

export type PreviewArea = (typeof previewAreas)[number];

export type AreaPage = {
  // The dashboard path the preview opens.
  path: string;
  // The visible height of the window at the drawn width of 1280 px.
  height: number;
};

export const areaPages = {
  pipelines: {
    path: `/app/pipelines/${raidPipelineId}?step=${stepIdOf('Raid response', 'play_tts')}`,
    height: 640,
  },
  moderation: { path: '/app/settings/spam-defense', height: 640 },
  widgets: { path: `/app/overlays?id=${widgetIdOf('Goal Bar')}`, height: 640 },
  analytics: { path: '/app/analytics', height: 640 },
  commands: { path: '/app/commands?command=hug', height: 780 },
  loyalty: { path: '/app/economy?view=rules', height: 640 },
  music: { path: '/app/music', height: 640 },
  platforms: { path: '/app/settings/integrations', height: 640 },
} as const satisfies Record<PreviewArea, AreaPage>;

export type GroupPage = AreaPage & {
  // The folder of the page in dashboard/src/views/dashboard, which the group links to on GitHub.
  source: string;
};

// The wide preview of a feature group: another view of the same page than the hero shows.
export const groupPages = {
  pipelines: {
    path: `/app/pipelines/${branchingPipelineId}?step=big-raid-shoutout`,
    height: 560,
    source: 'pipelines',
  },
  moderation: {
    path: '/app/settings/escalation',
    height: 560,
    source: 'settings/escalation',
  },
  widgets: {
    path: `/app/overlays?id=${widgetIdOf('Now Playing')}`,
    height: 560,
    source: 'overlays',
  },
  analytics: {
    path: '/app/analytics?view=chat',
    height: 560,
    source: 'analytics',
  },
  commands: {
    path: '/app/commands?command=luck',
    height: 560,
    source: 'commands',
  },
  loyalty: {
    path: '/app/economy?view=catalog',
    height: 560,
    source: 'economy',
  },
  music: {
    path: '/app/tts',
    height: 720,
    source: 'tts',
  },
  platforms: {
    path: '/app/settings/white-label-bot',
    height: 380,
    source: 'settings/white-label-bot',
  },
} as const satisfies Record<PreviewArea, GroupPage>;
