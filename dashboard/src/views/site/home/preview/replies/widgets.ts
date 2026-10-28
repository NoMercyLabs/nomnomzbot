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

import { merge } from '@/lib/array';

import type { FakeReply } from '../fakeApi';
import overlaysRequired from '../fixtures/overlays-required.json';
import widgetGallery from '../fixtures/widget-gallery.json';
import widgetSchemas from '../fixtures/widget-settings-schemas.json';
import widgetsCapture from '../fixtures/widgets.json';
import { channelPath, fixture, get, minutesAgo } from './shape';

// The first-party widgets a fresh install gets, with their real events and settings. When each one last ran
// is sample: a fresh install has not run any yet.
const seeded = fixture(widgetsCapture).data;

// A fresh install holds only the five first-party widgets. The sample channel also installed these from the
// gallery; the names, descriptions and gallery ids are the server's own, and the install is the sample part.
const installedFromGallery = ['Goal Bar', 'Chat Box', 'Now Playing', 'Event Ticker'];

const gallery = fixture(widgetGallery).data;

const installed = gallery
  .filter((item) => installedFromGallery.includes(item.name))
  .map((item, index) => ({
    ...(seeded[0] as (typeof seeded)[number]),
    id: `sample-widget-${index}`,
    name: item.name,
    description: item.description,
    framework: item.framework,
    galleryItemId: item.id,
    settings: {},
    eventSubscriptions: [],
    isCustomized: false,
  }));

const widgets = merge(seeded, installed).map((widget, index) => ({
  ...widget,
  // A fresh install has no browser source in OBS yet; the sample channel has added them all.
  isAttached: true,
  lastRuntimeError: null,
  lastRanAt: minutesAgo(3 + index * 4),
}));

const required = fixture(overlaysRequired);

// The id of a widget by its name, for the preview paths: ?id=<id>.
export function widgetIdOf(name: (typeof widgets)[number]['name']): string {
  return widgets.find((widget) => widget.name === name)?.id ?? '';
}

type SchemaName = keyof typeof widgetSchemas;

function schemaFor(url: URL) {
  const id = url.pathname.split('/').at(-2);
  const name = widgets.find((widget) => widget.id === id)?.name as SchemaName | undefined;

  return {
    status: 'ok',
    data: name === undefined
      ? { fields: [] }
      : widgetSchemas[name],
    args: [],
  };
}

export const widgetReplies: FakeReply[] = [
  get(`${channelPath}/widgets`, {
    data: widgets,
    hasMore: false,
    total: widgets.length,
  }),
  get('/api/v1/widget-gallery', fixture(widgetGallery)),
  get(`${channelPath}/overlays/required`, required),
  // The sample channel has no OBS control: the overlay rows read this to decide whether to warn.
  get(`${channelPath}/obs/connection`, {
    status: 'ok',
    data: {
      mode: 'direct',
      isEnabled: false,
      lastConnectedAt: null,
    },
  }),
  get(`${channelPath}/obs/bridge/status`, {
    status: 'ok',
    data: {
      instanceCount: 0,
      hasLeader: false,
      leaderSince: null,
    },
  }),
  get(`${channelPath}/widgets/*/settings-schema`, (url) =>
    schemaFor(url)),
];
