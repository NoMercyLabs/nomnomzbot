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

import { inviteRoute } from '../../views/site/invite/route';

type Router = typeof import('../index').default;

// The bot's answers: a refresh cookie that works or not, and an empty list for every other call.
function stubFetch(hasSession: boolean): void {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      if (request.url.endsWith('/auth/refresh'))
        return hasSession
          ? Response.json({ data: { accessToken: 'token-from-cookie', expiresIn: 3600 } })
          : new Response('{}', { status: 401 });

      return Response.json({ data: [] });
    }),
  );
}

async function freshRouter(): Promise<Router> {
  vi.resetModules();

  return (await import('../index')).default;
}

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
  history.replaceState(null, '', '/');
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the invite route', () => {
  it('opens for a signed-out visitor, with the code from the address', async () => {
    stubFetch(false);
    const router = await freshRouter();

    await router.push('/invite/WELCOME6');

    expect(router.currentRoute.value.name).toBe(inviteRoute.name);
    expect(router.currentRoute.value.params.code).toBe('WELCOME6');
  });

  it('stays on the invite page for a signed-in streamer, who is not sent on to the dashboard', async () => {
    stubFetch(true);
    const router = await freshRouter();

    await router.push('/invite/WELCOME6');

    expect(router.currentRoute.value.name).toBe(inviteRoute.name);
    expect(router.currentRoute.value.fullPath).toBe('/invite/WELCOME6');
  });
});
