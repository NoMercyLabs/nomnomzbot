// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

type Auth = typeof import('../auth');
type QueryClientModule = typeof import('../queryClient');

const user = { id: 'u1', displayName: 'Stoney_Eagle' };

// The server accepts only the fresh token; the old one has expired.
// `limited` names paths whose first call is refused with 429 and the given Retry-After seconds.
function stubServer(refreshStatus: number, limited: Record<string, string> = {}): string[] {
  const seen: string[] = [];

  vi.stubGlobal(
    'fetch',
    vi.fn(async (request: Request) => {
      const bearer = request.headers.get('Authorization') ?? 'none';
      const path = new URL(request.url).pathname;

      seen.push(`${path} ${bearer}`);
      const wait = limited[path];

      if (wait !== undefined) {
        delete limited[path];

        return new Response('{}', { status: 429, headers: { 'Retry-After': wait } });
      }

      if (request.url.endsWith('/auth/refresh'))
        return refreshStatus === 200
          ? new Response(JSON.stringify({ data: { accessToken: 'fresh', expiresIn: 3600 } }), {
              status: 200,
            })
          : new Response('{}', { status: refreshStatus });

      if (bearer !== 'Bearer fresh')
        return new Response('{}', { status: 401 });

      return new Response(JSON.stringify({ data: user }), { status: 200 });
    }),
  );

  return seen;
}

async function signedInWithExpiredToken(): Promise<[Auth, QueryClientModule]> {
  vi.resetModules();
  history.replaceState(null, '', '/app#access_token=expired');
  const auth = await import('../auth');
  const queries = await import('../queryClient');

  await auth.restoreSession();

  return [auth, queries];
}

beforeEach(() => {
  history.replaceState(null, '', '/');
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('two windows of one browser', () => {
  // A browser runs one holder of a lock name at a time, across every window of the site.
  function stubLocks(): void {
    let queue: Promise<unknown> = Promise.resolve();

    vi.stubGlobal('navigator', {
      ...navigator,
      locks: {
        request: (_name: string, task: () => Promise<unknown>) => {
          const run = queue.then(task);

          queue = run.catch(() => undefined);

          return run;
        },
      },
    });
  }

  it('never refresh at the same time, so the server never sees a used cookie twice', async () => {
    let open = 0;
    let overlapped = false;

    stubLocks();

    vi.stubGlobal('fetch', vi.fn(async () => {
      open += 1;
      overlapped ||= open > 1;

      await new Promise((resolve) =>
        setTimeout(resolve, 5));

      open -= 1;

      return new Response(JSON.stringify({ data: { accessToken: 'fresh' } }), { status: 200 });
    }));

    vi.resetModules();
    const main = await import('../auth');

    vi.resetModules();
    const popout = await import('../auth');

    await Promise.all([main.restoreSession(), popout.restoreSession()]);

    expect(overlapped).toBe(false);
  });
});

describe('expired access token', () => {
  it('refreshes once and repeats the call with the new token', async () => {
    const seen = stubServer(200);
    const [auth, { queryClient }] = await signedInWithExpiredToken();

    const result = await queryClient.query(auth.currentUserQuery);

    expect(result).toEqual(user);

    expect(seen).toEqual([
      '/api/v1/auth/me Bearer expired',
      '/api/v1/auth/refresh none',
      '/api/v1/auth/me Bearer fresh',
    ]);

    expect(auth.isSignedIn()).toBe(true);
  });

  it('signs the visitor out when the refresh cookie is gone too', async () => {
    const seen = stubServer(401);
    const [auth, { queryClient }] = await signedInWithExpiredToken();

    const result = await queryClient.query(auth.currentUserQuery);

    expect(result).toBeNull();
    expect(seen).toHaveLength(2);
    expect(auth.isSignedIn()).toBe(false);
  });
});

// 2026-10-18: a browser that had picked a bot address nobody runs (CORS-blocked) threw out of the router
// on start and showed a blank page instead of the sign-in.
describe('a bot that cannot be reached', () => {
  it('leaves the visitor signed out instead of throwing', async () => {
    vi.stubGlobal('fetch', vi.fn(async () => {
      throw new TypeError('Failed to fetch');
    }));

    vi.resetModules();
    const auth = await import('../auth');

    expect(await auth.restoreSession()).toBe(false);
    expect(auth.isSignedIn()).toBe(false);
  });
});

// Stoney 2026-10-16: the login page called auth/refresh every ~5 s after a 401 and ran into the rate limit.
describe('a refused refresh', () => {
  it('is asked once per page load, not again on every 401', async () => {
    const seen = stubServer(401);

    vi.resetModules();
    const auth = await import('../auth');

    expect(await auth.restoreSession()).toBe(false);
    expect(await auth.restoreSession()).toBe(false);
    expect(await auth.restoreSession()).toBe(false);

    expect(seen.filter((call) => call.startsWith('/api/v1/auth/refresh'))).toHaveLength(1);
  });
});

// Stoney 2026-10-14: "I'm redirected from Vue to dev instead back to Vue again". A sign-in started on another
// listed site comes back there with a one-time code; this site trades it for the token and its own cookie.
describe('a sign-in that returns with a login code', () => {
  function stubExchange(status: number): {
    path: string;
    body: unknown;
    credentials: string;
  }[] {
    const seen: {
      path: string;
      body: unknown;
      credentials: string;
    }[] = [];

    vi.stubGlobal(
      'fetch',
      vi.fn(async (request: Request) => {
        const path = new URL(request.url).pathname;

        seen.push({
          path,
          body: await request.clone().json().catch(() => null),
          credentials: request.credentials,
        });

        if (path.endsWith('/auth/web/exchange'))
          return status === 200
            ? new Response(JSON.stringify({ data: {
                accessToken: 'traded',
                expiresIn: 3600,
                user,
              } }), {
                status: 200,
              })
            : new Response('{}', { status });

        return new Response('{}', { status: 401 });
      }),
    );

    return seen;
  }

  it('trades the code once for the token and strips it from the address', async () => {
    const seen = stubExchange(200);

    vi.resetModules();
    history.replaceState(null, '', '/app/chat?x=1#login_code=one-time');
    const auth = await import('../auth');

    expect(await auth.restoreSession()).toBe(true);
    expect(auth.getAccessToken()).toBe('traded');
    expect(window.location.hash).toBe('');
    expect(window.location.pathname + window.location.search).toBe('/app/chat?x=1');

    expect(seen).toEqual([{
      path: '/api/v1/auth/web/exchange',
      body: { code: 'one-time' },
      credentials: 'include',
    }]);
  });

  it('stays signed out when the code is spent or expired, and does not try it again', async () => {
    const seen = stubExchange(401);

    vi.resetModules();
    history.replaceState(null, '', '/app#login_code=spent');
    const auth = await import('../auth');

    expect(await auth.restoreSession()).toBe(false);
    expect(auth.isSignedIn()).toBe(false);
    expect(window.location.hash).toBe('');
    expect(seen.map((call) => call.path)).toEqual(['/api/v1/auth/web/exchange', '/api/v1/auth/refresh']);
  });
});

describe('rate limited by the server', () => {
  it('keeps the visitor signed in when the refresh is refused with 429 once', async () => {
    const seen: string[] = stubServer(200, { '/api/v1/auth/refresh': '0' });
    const [auth, { queryClient }] = await signedInWithExpiredToken();

    const result = await queryClient.query(auth.currentUserQuery);

    expect(result).toEqual(user);

    expect(seen).toEqual([
      '/api/v1/auth/me Bearer expired',
      '/api/v1/auth/refresh none',
      '/api/v1/auth/refresh none',
      '/api/v1/auth/me Bearer fresh',
    ]);

    expect(auth.isSignedIn()).toBe(true);
  });

  it('asks for the current user again with the same token after a 429', async () => {
    const seen: string[] = stubServer(200, { '/api/v1/auth/me': '0' });

    vi.resetModules();
    history.replaceState(null, '', '/app#access_token=fresh');
    const auth = await import('../auth');
    const { queryClient }: QueryClientModule = await import('../queryClient');

    await auth.restoreSession();

    const result = await queryClient.query(auth.currentUserQuery);

    expect(result).toEqual(user);
    expect(seen).toEqual(['/api/v1/auth/me Bearer fresh', '/api/v1/auth/me Bearer fresh']);
  });

  it('gives up instead of hanging when the server asks for a long wait', async () => {
    const seen: string[] = stubServer(200, { '/api/v1/auth/me': '120' });

    vi.resetModules();
    history.replaceState(null, '', '/app#access_token=fresh');
    const auth = await import('../auth');
    const { queryClient }: QueryClientModule = await import('../queryClient');

    await auth.restoreSession();

    const result = await queryClient.query(auth.currentUserQuery);

    expect(result).toBeNull();
    expect(seen).toHaveLength(1);
    expect(auth.isSignedIn()).toBe(true);
  });
});
