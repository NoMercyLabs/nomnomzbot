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

import { notFoundRoute } from '../../views/site/not-found/route';

type Router = typeof import('../index').default;

async function freshRouter(): Promise<Router> {
  vi.resetModules();

  return (await import('../index')).default;
}

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
  history.replaceState(null, '', '/');

  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) =>
      request.url.endsWith('/auth/refresh')
        ? new Response('{}', { status: 401 })
        : Response.json({ data: [] })),
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('the not-found route', () => {
  it('matches an address no page owns, so the visitor sees the 404 page and not a blank screen', async () => {
    const router = await freshRouter();

    await router.push('/no-such-page');

    expect(router.currentRoute.value.name).toBe(notFoundRoute.name);
    expect(router.currentRoute.value.matched).toHaveLength(1);
  });
});
