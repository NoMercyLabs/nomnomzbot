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

import { beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent } from 'vue';
import { createMemoryHistory, createRouter, type RouteRecordRaw, type Router } from 'vue-router';

import { signInGuard } from '../guard';
import {
  channelPrefixGuard,
  channelVariants,
  ownChannelGuard,
  pageLocation,
  platformPathGuard,
  switchLocation,
  type ChannelParams,
} from '../channelRoutes';

// What the bot says: whether a session exists, and the login of the channel being managed.
const held = vi.hoisted(() => ({
  signedIn: true,
  login: 'mychannel' as string | null,
}));

vi.mock('@/api/auth', () => ({
  restoreSession: () => Promise.resolve(held.signedIn),
  takeOAuthLanding: () => false,
}));

vi.mock('@/composables/useCurrentChannel', () => ({
  currentChannelLogin: () => Promise.resolve(held.login),
}));

const page = defineComponent({ render: () => null });
const pages: RouteRecordRaw[] = [
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
  {
    path: 'admin/channels',
    name: 'dashboard-admin-channels',
    meta: { navMode: 'admin' },
    component: page,
  },
  {
    path: 'settings',
    name: 'dashboard-settings',
    meta: { userLevel: true },
    component: page,
  },
  {
    path: 'settings/bot-basics',
    name: 'dashboard-settings-bot-basics',
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

  it('goes to the prefixed address for the own channel too', async () => {
    const router = makeRouter();

    await router.push('/app/u/friend/stream');
    await router.push(switchLocation(router.currentRoute.value, 'mychannel'));

    expect(router.currentRoute.value.fullPath).toBe('/app/u/mychannel/stream');
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

const loginRoute = {
  path: '/login',
  name: 'login',
  component: page,
};

function makeGuardedRouter(): Router {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      loginRoute,
      {
        path: '/me',
        name: 'viewer-me',
        component: page,
        meta: { requiresAuth: true },
      },
      {
        path: '/app',
        component: page,
        meta: { requiresAuth: true },
        children: pages,
      },
      {
        path: '/app/u/:channelName',
        component: page,
        meta: { requiresAuth: true },
        children: channelVariants(pages),
      },
    ],
  });

  router.beforeEach(platformPathGuard);
  router.beforeEach(channelPrefixGuard);
  router.beforeEach(signInGuard);
  router.beforeEach(ownChannelGuard);

  return router;
}

describe('opening the dashboard without a channel prefix', () => {
  beforeEach(() => {
    held.signedIn = true;
    held.login = 'mychannel';
  });

  it('sends /app to /app/u/<current channel>', async () => {
    const router = makeGuardedRouter();

    await router.push('/app');

    expect(router.currentRoute.value.fullPath).toBe('/app/u/mychannel');
    expect(router.currentRoute.value.name).toBe('u:dashboard-home');
  });

  it('keeps the page, the query and the hash', async () => {
    const router = makeGuardedRouter();

    await router.push('/app/stream?x=1#y');

    expect(router.currentRoute.value.fullPath).toBe('/app/u/mychannel/stream?x=1#y');
  });

  it('replaces the entry, so back does not return to the short address', async () => {
    const router = makeGuardedRouter();

    await router.push('/app/stream');

    expect(router.options.history.state.back ?? null).toBeNull();
  });

  it('leaves an address that already names a channel as it is', async () => {
    const router = makeGuardedRouter();

    await router.push('/app/u/friend/stream');

    expect(router.currentRoute.value.fullPath).toBe('/app/u/friend/stream');
  });

  it('keeps the short address for a person with no channel', async () => {
    held.login = null;
    const router = makeGuardedRouter();

    await router.push('/app/stream');

    expect(router.currentRoute.value.fullPath).toBe('/app/stream');
  });

  it('lands a person with no own and no moderated channel on My channels', async () => {
    held.login = null;
    const router = makeGuardedRouter();

    await router.push('/app');

    expect(router.currentRoute.value.fullPath).toBe('/me');
    expect(router.currentRoute.value.name).toBe('viewer-me');
  });

  it('lands a person with an own channel on that channel', async () => {
    held.login = 'mychannel';
    const router = makeGuardedRouter();

    await router.push('/app');

    expect(router.currentRoute.value.fullPath).toBe('/app/u/mychannel');
  });

  it('keeps the login flow for a signed-out visitor, with no loop', async () => {
    held.signedIn = false;
    const router = makeGuardedRouter();

    await router.push('/app/stream');

    expect(router.currentRoute.value.path).toBe('/login');
    expect(router.currentRoute.value.query.return_to).toBe('/app/stream');
  });
});

describe('pages that belong to no channel', () => {
  beforeEach(() => {
    held.signedIn = true;
    held.login = 'mychannel';
  });

  it('has no channel twin for the admin pages or the per-person settings', () => {
    const twins = channelVariants(pages).map((twin) => twin.name);

    expect(twins).not.toContain('u:dashboard-admin-channels');
    expect(twins).not.toContain('u:dashboard-settings');
    expect(twins).toContain('u:dashboard-settings-bot-basics');
  });

  it('keeps /app/admin/... at its own address', async () => {
    const router = makeGuardedRouter();

    await router.push('/app/admin/channels?x=1');

    expect(router.currentRoute.value.fullPath).toBe('/app/admin/channels?x=1');
  });

  it('keeps the per-person settings at /app/settings', async () => {
    const router = makeGuardedRouter();

    await router.push('/app/settings?view=device');

    expect(router.currentRoute.value.fullPath).toBe('/app/settings?view=device');
  });

  it('keeps the admin address when the move starts on a channel route', async () => {
    const router = makeGuardedRouter();

    await router.push('/app/u/friend/stream');
    await router.push('/app/admin/channels');

    expect(router.currentRoute.value.fullPath).toBe('/app/admin/channels');
  });

  it('sends an old /app/u/<name>/admin/... address to /app/admin/...', async () => {
    const router = makeGuardedRouter();

    await router.push('/app/u/friend/admin/channels?x=1#y');

    expect(router.currentRoute.value.fullPath).toBe('/app/admin/channels?x=1#y');
  });

  it('sends an old /app/u/<name>/settings address to /app/settings', async () => {
    const router = makeGuardedRouter();

    await router.push('/app/u/friend/settings');

    expect(router.currentRoute.value.fullPath).toBe('/app/settings');
  });

  it('leaves a channel settings page under its prefix', async () => {
    const router = makeGuardedRouter();

    await router.push('/app/settings/bot-basics');

    expect(router.currentRoute.value.fullPath).toBe('/app/u/mychannel/settings/bot-basics');
  });
});

describe('a link to a page', () => {
  it('names the channel twin when a channel is known', () => {
    expect(pageLocation('dashboard-music', 'mychannel')).toEqual({
      name: 'u:dashboard-music',
      params: { channelName: 'mychannel' },
    });
  });

  it('keeps the short page when no channel is known', () => {
    expect(pageLocation('dashboard-music', null)).toEqual({ name: 'dashboard-music' });
  });
});
