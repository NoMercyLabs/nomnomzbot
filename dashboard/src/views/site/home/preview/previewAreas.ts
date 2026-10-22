// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { raidPipelineId } from './replies/pipelines';

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
  pipelines: { path: `/app/pipelines/${raidPipelineId}?step=welcome`, height: 640 },
  moderation: { path: '/app/settings/spam-defense', height: 640 },
  widgets: { path: '/app/overlays?id=sample-widget-hype', height: 640 },
  analytics: { path: '/app/analytics', height: 640 },
  commands: { path: '/app/commands?command=hug', height: 640 },
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
    path: `/app/pipelines/${raidPipelineId}?step=shout`,
    height: 560,
    source: 'pipelines',
  },
  moderation: {
    path: '/app/settings/spam-defense',
    height: 560,
    source: 'settings/spam-defense',
  },
  widgets: {
    path: '/app/overlays?id=sample-widget-nowplaying',
    height: 560,
    source: 'overlays',
  },
  analytics: {
    path: '/app/analytics?view=chat',
    height: 560,
    source: 'analytics',
  },
  commands: {
    path: '/app/commands?command=uptime',
    height: 560,
    source: 'commands',
  },
  loyalty: {
    path: '/app/economy?view=catalog',
    height: 560,
    source: 'economy',
  },
  music: {
    path: '/app/music',
    height: 720,
    source: 'music',
  },
  platforms: {
    path: '/app/settings/integrations',
    height: 560,
    source: 'settings/integrations',
  },
} as const satisfies Record<PreviewArea, GroupPage>;
