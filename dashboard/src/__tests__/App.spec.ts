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

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { dashboardHomeRoute } from '../views/dashboard/home/route';
import { loginRoute } from '../views/site/login/route';
import { viewerChannelsRoute } from '../views/viewer/channels/route';

type Router = typeof import('../router').default;

const refreshOk = { data: { accessToken: 'token-from-cookie', expiresIn: 3600 } };

// A signed-in person's other calls answer with an empty list, so the access guard reads no channel.
function stubFetch(refreshStatus: number, otherCallsOk = false): void {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      if (request.url.endsWith('/auth/refresh') && refreshStatus === 200)
        return new Response(JSON.stringify(refreshOk), { status: 200 });

      if (otherCallsOk && !request.url.endsWith('/auth/refresh'))
        return new Response(JSON.stringify({ data: [] }), { status: 200 });

      return new Response('{}', { status: refreshStatus });
    }),
  );
}

async function freshRouter(): Promise<Router> {
  vi.resetModules();
  const router = (await import('../router')).default;

  return router;
}

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
  history.replaceState(null, '', '/');
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('sign-in guard', () => {
  it('sends a signed-out visitor to the login page and remembers where they were going', async () => {
    stubFetch(401);
    const router = await freshRouter();

    await router.push('/app');

    expect(router.currentRoute.value.name).toBe(loginRoute.name);
    expect(router.currentRoute.value.query.return_to).toBe('/app');
    const { i18n } = await import('../i18n');

    expect(i18n.global.t('site.login.title')).toBe('Welcome');
  });

  // A person with no own and no moderated channel lands on My channels from the bare /app (c644e2204).
  it('lets a visitor with a refresh cookie into the app, on My channels when they have no channel', async () => {
    stubFetch(200);
    const router = await freshRouter();

    await router.push('/app');

    expect(router.currentRoute.value.name).toBe(viewerChannelsRoute.name);
  });

  it('takes the token from the OAuth fragment, strips it from the address and never stores it', async () => {
    stubFetch(401, true);
    history.replaceState(null, '', '/app#access_token=from-fragment&expires_in=3600');
    const router = await freshRouter();

    // The first navigation carries the whole address the browser opened, fragment included.
    await router.push(window.location.pathname + window.location.hash);

    const auth = await import('../api/auth');

    expect(router.currentRoute.value.name).toBe(dashboardHomeRoute.name);
    expect(auth.isSignedIn()).toBe(true);
    expect(window.location.hash).toBe('');
    const stored = JSON.stringify({ ...localStorage, ...sessionStorage });

    expect(stored).not.toContain('from-fragment');
  });

  // Stoney 2026-10-13: "the login screen should redirect to the app if the user has a valid session".
  it('sends a visitor with a valid session from the login page into the app', async () => {
    stubFetch(200);
    const router = await freshRouter();

    await router.push({ name: loginRoute.name });

    expect(router.currentRoute.value.name).toBe(viewerChannelsRoute.name);
  });

  it('sends a signed-in visitor on to the page the login was asked for', async () => {
    stubFetch(200);
    const router = await freshRouter();

    await router.push({ name: loginRoute.name, query: { return_to: '/app/commands' } });

    expect(router.currentRoute.value.path).toBe('/app/commands');
  });

  it('never follows a return address to another site', async () => {
    stubFetch(200);
    const router = await freshRouter();

    await router.push({ name: loginRoute.name, query: { return_to: '//evil.example' } });

    expect(router.currentRoute.value.name).toBe(viewerChannelsRoute.name);
    expect(router.currentRoute.value.path).not.toContain('evil');
  });

  it('keeps a signed-out visitor on the login page', async () => {
    stubFetch(401);
    const router = await freshRouter();

    await router.push({ name: loginRoute.name });

    expect(router.currentRoute.value.name).toBe(loginRoute.name);
  });
});
