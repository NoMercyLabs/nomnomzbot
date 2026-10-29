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
  // The dashboard path the capture page opens (dashboard/capture.html).
  path: string;
};

// The two pictures of an area: the hero tab (with the sidebar) and the feature group below it (without).
export type ShowcaseKind = 'hero' | 'group';

// The still picture of an area, photographed from the real page at 1920x1080 (see public/showcase/README.md).
export function showcaseImage(area: PreviewArea, kind: ShowcaseKind): string {
  return `/showcase/${area}-${kind}.webp`;
}

export const areaPages = {
  pipelines: {
    path: `/app/pipelines/${raidPipelineId}?step=${stepIdOf('Raid response', 'play_tts')}`,
  },
  moderation: { path: '/app/settings/spam-defense' },
  widgets: { path: `/app/overlays?id=${widgetIdOf('Goal Bar')}` },
  analytics: { path: '/app/analytics' },
  commands: { path: '/app/commands?command=hug' },
  loyalty: { path: '/app/economy?view=rules' },
  music: { path: '/app/music' },
  platforms: { path: '/app/settings/integrations' },
} as const satisfies Record<PreviewArea, AreaPage>;

export type GroupPage = AreaPage & {
  // The folder of the page in dashboard/src/views/dashboard, which the group links to on GitHub.
  source: string;
};

// The wide preview of a feature group: another view of the same page than the hero shows.
export const groupPages = {
  pipelines: {
    path: `/app/pipelines/${branchingPipelineId}?step=big-raid-shoutout`,
    source: 'pipelines',
  },
  moderation: {
    path: '/app/settings/escalation',
    source: 'settings/escalation',
  },
  widgets: {
    path: `/app/overlays?id=${widgetIdOf('Now Playing')}`,
    source: 'overlays',
  },
  analytics: {
    path: '/app/analytics?view=chat',
    source: 'analytics',
  },
  commands: {
    path: '/app/commands?command=luck',
    source: 'commands',
  },
  loyalty: {
    path: '/app/economy?view=catalog',
    source: 'economy',
  },
  music: {
    path: '/app/tts',
    source: 'tts',
  },
  platforms: {
    path: '/app/settings/white-label-bot',
    source: 'settings/white-label-bot',
  },
} as const satisfies Record<PreviewArea, GroupPage>;
