// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { queryOptions, useQuery, type UseQueryReturnType } from '@tanstack/vue-query';
import { ref, type Ref } from 'vue';

import type { components } from './schema';
import { activeBot } from './activeBot';
import { client } from './client';
import { queryClient } from './queryClient';

export type CurrentUser = components['schemas']['CurrentUserDto'];

const version = { path: { version: '1' } };

// The access token lives in this module only. The refresh token is the server's httpOnly cookie.
let accessToken: string | null = null;
// Follows the sign-in for the live connection; a token swapped by the 401 retry does not move it.
const signedIn = ref(false);
let restoring: Promise<boolean> | null = null;

// A copy of each request, kept until its response, so a 401 can be sent again with a new token.
const pending = new Map<string, Request>();

// A 429 is "not now", never "signed out": wait as long as the server asks, then ask again.
// A wait longer than this hands the 429 back instead of freezing the page.
const longestRateLimitWait: number = 10;
const rateLimitTries: number = 2;

// A 401 from a sign-in call is its answer, never a reason to sign in again: that would wait on itself.
const signInCalls = ['/auth/refresh', '/auth/web/exchange'] as const;

async function afterRateLimit(first: Response, original: Request): Promise<Response> {
  let response: Response = first;

  for (let tries: number = 0; response.status === 429 && tries < rateLimitTries; tries++) {
    const seconds: number = Number(response.headers.get('Retry-After') ?? '1');

    if (!Number.isFinite(seconds) || seconds > longestRateLimitWait)
      return response;

    await new Promise<void>((resolve) => setTimeout(resolve, Math.max(seconds, 0) * 1000));
    response = await fetch(original.clone());
  }

  return response;
}

client.use({
  onRequest({ request, id }) {
    if (accessToken) {
      request.headers.set('Authorization', `Bearer ${accessToken}`);
    }

    pending.set(id, request.clone());

    return request;
  },
  async onResponse({ request, response: first, id }) {
    const original = pending.get(id);

    pending.delete(id);
    const response = original
      ? await afterRateLimit(first, original)
      : first;

    const isSignInCall = signInCalls.some((path) =>
      request.url.endsWith(path));

    if (response.status !== 401 || !original || isSignInCall)
      return response;

    accessToken = null;

    if (!(await restoreSession()))
      return response;

    original.headers.set('Authorization', `Bearer ${accessToken}`);

    return fetch(original);
  },
  onError({ id }) {
    pending.delete(id);
  },
});

export function isSignedIn(): boolean {
  return accessToken !== null;
}

export function useSignedIn(): Readonly<Ref<boolean>> {
  return signedIn;
}

// The live connection asks for the token on every connect, so a refreshed one is used at once.
export function getAccessToken(): string {
  return accessToken ?? '';
}

// A sign-in that finished on this page (the device code) hands its access token over here.
export function acceptAccessToken(token: string): void {
  accessToken = token;
  signedIn.value = true;
}

function stripFragment(): void {
  history.replaceState(history.state, '', window.location.pathname + window.location.search);
}

// The OAuth callback returns the access token in the URL fragment; take it once and strip it.
function consumeFragment(): boolean {
  const fragment = new URLSearchParams(window.location.hash.slice(1));
  const token = fragment.get('access_token');

  if (!token)
    return false;

  accessToken = token;
  signedIn.value = true;
  stripFragment();

  return true;
}

// A sign-in started on another listed site of this bot comes back with a one-time code instead of a token.
// This site trades it for the token, and the bot sets the refresh cookie on this site. A spent code is dropped.
function takeLoginCode(): string | null {
  const code = new URLSearchParams(window.location.hash.slice(1)).get('login_code');

  if (code) {
    stripFragment();
  }

  return code;
}

async function exchange(code: string): Promise<boolean> {
  const { data } = await client.POST('/api/v{version}/auth/web/exchange', {
    params: version,
    body: { code },
    credentials: 'include',
  });
  const token = data?.data?.accessToken;

  if (!token)
    return false;

  acceptAccessToken(token);

  return true;
}

async function signInAgain(): Promise<boolean> {
  const code = takeLoginCode();

  if (code && await exchange(code))
    return true;

  return refresh();
}

// Every window of the site shares one refresh cookie, and the bot revokes the whole session when a used
// cookie comes back. Two windows (the dashboard and a popped-out chat) refreshing at once did exactly that,
// so the refresh takes a browser-wide lock: the second window waits and sends the cookie the first got back.
async function refresh(): Promise<boolean> {
  return navigator.locks?.request
    ? await navigator.locks.request('nnz-auth-refresh', refreshNow)
    : await refreshNow();
}

type RefreshEnvelope = { data?: { accessToken?: string } | null };

async function refreshNow(): Promise<boolean> {
  const result: { data?: RefreshEnvelope; response: Response } = await client.POST(
    '/api/v{version}/auth/refresh',
    { params: version, credentials: 'include' },
  );
  const { response } = result;
  // The OpenAPI document declares no 200 body for refresh yet, so the envelope is typed here.
  const token: string | undefined = result.data?.data?.accessToken;

  if (!response.ok || typeof token !== 'string')
    return false;

  accessToken = token;
  signedIn.value = true;

  return true;
}

export function restoreSession(): Promise<boolean> {
  if (accessToken)
    return Promise.resolve(true);

  if (consumeFragment())
    return Promise.resolve(true);

  restoring ??= signInAgain().finally(() => {
    restoring = null;
  });

  return restoring;
}

async function fetchCurrentUser(): Promise<CurrentUser | null> {
  const { data } = await client.GET('/api/v{version}/auth/me', { params: version });

  return data?.data ?? null;
}

export const currentUserQuery = queryOptions({
  queryKey: ['auth', 'me'],
  queryFn: fetchCurrentUser,
});

export function useCurrentUser(): UseQueryReturnType<CurrentUser | null, Error> {
  return useQuery(currentUserQuery);
}

// A sign-in and its answers belong to one bot; another bot starts signed out with an empty cache.
export function switchBot(origin: string): void {
  if (origin === activeBot.value) return;

  accessToken = null;
  signedIn.value = false;
  queryClient.clear();
  activeBot.value = origin;
}

export async function signOut(): Promise<void> {
  await client.POST('/api/v{version}/auth/logout', { params: version, credentials: 'include' });
  accessToken = null;
  signedIn.value = false;
  queryClient.clear();
}
