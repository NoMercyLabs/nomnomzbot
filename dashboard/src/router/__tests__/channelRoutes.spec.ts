// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest';
import { defineComponent } from 'vue';
import { createMemoryHistory, createRouter, type Router } from 'vue-router';

import {
  channelPrefixGuard,
  channelVariants,
  switchLocation,
  type ChannelParams,
} from '../channelRoutes';

const page = defineComponent({ render: () => null });
const pages = [
  {
    path: '',
    name: 'dashboard-home',
    component: page,
  },
  {
    path: 'stream',
    name: 'dashboard-stream',
    component: page,
  },
  {
    path: 'pipelines/:id',
    name: 'dashboard-pipeline',
    component: page,
  },
];

function makeRouter(): Router {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      {
        path: '/app',
        component: page,
        children: pages,
      },
      {
        path: '/app/u/:channelName',
        component: page,
        children: channelVariants(pages),
      },
    ],
  });

  router.beforeEach(channelPrefixGuard);

  return router;
}

describe('a channel link', () => {
  it('opens the page at /app/u/<name>/<page> and carries the channel name as a param', async () => {
    const router = makeRouter();

    await router.push('/app/u/friend/stream');

    expect(router.currentRoute.value.name).toBe('u:dashboard-stream');
    expect(router.currentRoute.value.params.channelName).toBe('friend');
  });

  it('opens the channel home at /app/u/<name>', async () => {
    const router = makeRouter();

    await router.push('/app/u/friend');

    expect(router.currentRoute.value.name).toBe('u:dashboard-home');
  });

  it('leaves the short route as it was', async () => {
    const router = makeRouter();

    await router.push('/app/stream');

    expect(router.currentRoute.value.name).toBe('dashboard-stream');
    expect(router.currentRoute.value.fullPath).toBe('/app/stream');
  });
});

describe('moving inside a channel route', () => {
  it('keeps the prefix for a named link', async () => {
    const router = makeRouter();

    await router.push('/app/u/friend/stream');
    await router.push({ name: 'dashboard-home' });

    expect(router.currentRoute.value.fullPath).toBe('/app/u/friend');
  });

  it('keeps the prefix, the query and the hash for a path link', async () => {
    const router = makeRouter();

    await router.push('/app/u/friend/stream');
    await router.push('/app/stream?x=1#top');

    expect(router.currentRoute.value.fullPath).toBe('/app/u/friend/stream?x=1#top');
  });

  it('adds no prefix when the visitor is on the short route', async () => {
    const router = makeRouter();

    await router.push('/app/stream');
    await router.push({ name: 'dashboard-home' });

    expect(router.currentRoute.value.fullPath).toBe('/app');
  });
});

describe('switching the channel', () => {
  it('rewrites the prefix on a channel route', async () => {
    const router = makeRouter();

    await router.push('/app/u/friend/stream?x=1');
    await router.push(switchLocation(router.currentRoute.value, 'other'));

    expect(router.currentRoute.value.fullPath).toBe('/app/u/other/stream?x=1');
  });

  it('goes to the short form for the own channel and the guard lets it stay short', async () => {
    const router = makeRouter();

    await router.push('/app/u/friend/stream');
    await router.push(switchLocation(router.currentRoute.value, null));

    expect(router.currentRoute.value.fullPath).toBe('/app/stream');
  });

  it('adds the prefix when another channel is chosen on a short route', async () => {
    const router = makeRouter();

    await router.push('/app/stream');
    await router.push(switchLocation(router.currentRoute.value, 'friend'));

    expect(router.currentRoute.value.fullPath).toBe('/app/u/friend/stream');
  });

  it('falls back to the page list when the page belongs to one item of the old channel', async () => {
    const router = makeRouter();

    await router.push('/app/u/friend/pipelines/abc');
    await router.push(switchLocation(router.currentRoute.value, 'other'));

    expect(router.currentRoute.value.fullPath).toBe('/app/u/other');
  });

  it('types the param record with the channel name only', () => {
    const params = { channelName: 'friend' } as const satisfies ChannelParams;

    expect(Object.keys(params)).toEqual(['channelName']);
  });
});
