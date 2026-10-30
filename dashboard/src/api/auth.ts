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

import { queryOptions, useQuery, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, ref, type ComputedRef, type Ref } from 'vue';

import type { components } from './schema';
import { activeBot } from './activeBot';
import { client } from './client';
import { problemError } from './errors';
import { queryClient } from './queryClient';

export type CurrentUser = components['schemas']['CurrentUser'];

const version = { path: { version: '1' } };

// The access token lives in this module only. The refresh token is the server's httpOnly cookie.
// A dev build keeps it for the tab too: a hand-made sign-in has no cookie, so every hot reload signed it out.
const devTokenKey = 'nnz-dev-access-token';
const keepsDevToken = import.meta.env.DEV && import.meta.env.MODE !== 'test';
let accessToken: string | null = keepsDevToken
  ? sessionStorage.getItem(devTokenKey)
  : null;

// The admin behind an act-as token: the bot names them in its `act` claim. Null for a person's own sign-in.
function actorOf(token: string | null): string | null {
  const payload = token?.split('.')[1];

  if (payload === undefined)
    return null;

  try {
    const decoded = atob(payload.replaceAll('-', '+').replaceAll('_', '/'));
    const claims = JSON.parse(decoded) as { act?: string };

    return typeof claims.act === 'string' && claims.act !== ''
      ? claims.act
      : null;
  } catch {
    return null;
  }
}

// Follows the sign-in for the live connection; a token swapped by the 401 retry does not move it.
const signedIn = ref(accessToken !== null);
const actingAs = ref<string | null>(actorOf(accessToken));
const identityChanges = ref(0);

// Acting as someone, or leaving that (Exit, or the session running out), changes the person behind every
// answer: the cached answers go, the open pages ask again, and the live connection reconnects.
function followIdentity(token: string): void {
  const actor = actorOf(token);

  if (actor === actingAs.value) return;

  actingAs.value = actor;

  if (!signedIn.value) return;

  identityChanges.value += 1;
  void queryClient.resetQueries();
}

function setAccessToken(token: string | null): void {
  accessToken = token;

  if (token !== null) {
    followIdentity(token);
  }

  if (!keepsDevToken) return;

  if (token) {
    sessionStorage.setItem(devTokenKey, token);
  } else {
    sessionStorage.removeItem(devTokenKey);
  }
}

let restoring: Promise<boolean> | null = null;
let refreshRefused = false;

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

    setAccessToken(null);

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

// True while an admin acts as someone else: the token is the target's, and only the Exit shows the admin.
export function useActingAs(): ComputedRef<boolean> {
  return computed(() => actingAs.value !== null);
}

// Counts the times the person behind the token changed while signed in; the live connection follows it.
export function useIdentityChanges(): Readonly<Ref<number>> {
  return identityChanges;
}

// The live connection asks for the token on every connect, so a refreshed one is used at once.
export function getAccessToken(): string {
  return accessToken ?? '';
}

// A sign-in that finished on this page (the device code) hands its access token over here.
export function acceptAccessToken(token: string): void {
  setAccessToken(token);
  refreshRefused = false;
  signedIn.value = true;
}

function stripFragment(): void {
  history.replaceState(history.state, '', window.location.pathname + window.location.search);
}

// True once after the OAuth callback landed on this page. The landing goes straight to the page it asked for:
// the first call with the new token is that page's own, never a side lookup the router makes on the way.
let landedFromOAuth = false;

export function takeOAuthLanding(): boolean {
  const landed = landedFromOAuth;

  landedFromOAuth = false;

  return landed;
}

// What happened to the invite a streamer sign-in carried: the bot redeemed it, or says why it did not.
// A failed redeem never blocks the sign-in, so this is only a message for the page the person lands on.
export type InviteOutcome = { redeemed: true } | { redeemed: false; reason: string };

let inviteOutcome: InviteOutcome | null = null;

function readInviteOutcome(fragment: URLSearchParams): void {
  const result = fragment.get('invite');

  if (result === 'redeemed') {
    inviteOutcome = { redeemed: true };
  }

  if (result === 'failed') {
    inviteOutcome = { redeemed: false, reason: fragment.get('invite_reason') ?? '' };
  }
}

// True once per sign-in that carried an invite: the message is shown a single time.
export function takeInviteOutcome(): InviteOutcome | null {
  const outcome = inviteOutcome;

  inviteOutcome = null;

  return outcome;
}

// The OAuth callback returns the access token in the URL fragment; take it once and strip it.
function consumeFragment(): boolean {
  const fragment = new URLSearchParams(window.location.hash.slice(1));
  const token = fragment.get('access_token');

  if (!token)
    return false;

  readInviteOutcome(fragment);
  setAccessToken(token);
  signedIn.value = true;
  landedFromOAuth = true;
  stripFragment();

  return true;
}

// A sign-in started on another listed site of this bot comes back with a one-time code instead of a token.
// This site trades it for the token, and the bot sets the refresh cookie on this site. A spent code is dropped.
function takeLoginCode(): string | null {
  const fragment = new URLSearchParams(window.location.hash.slice(1));
  const code = fragment.get('login_code');

  if (code) {
    readInviteOutcome(fragment);
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

  // A refresh the bot refused with 401 has no cookie to give; asking again on every 401 only
  // fills the rate limit. One answer per page load stands until a sign-in succeeds.
  if (refreshRefused)
    return false;

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
  // A bot that cannot be reached (offline, or a remembered address that blocks this origin) means signed
  // out: the sign-in page can pick another bot, where a thrown fetch would break the router start.
  const result: { data?: RefreshEnvelope; response: Response } | null = await client
    .POST('/api/v{version}/auth/refresh', { params: version, credentials: 'include' })
    .catch(() => null);

  if (result === null)
    return false;

  const { response } = result;
  // The OpenAPI document declares no 200 body for refresh yet, so the envelope is typed here.
  const token = result.data?.data?.accessToken;

  if (!response.ok || typeof token !== 'string') {
    refreshRefused = response.status === 401;

    return false;
  }

  refreshRefused = false;
  setAccessToken(token);
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

// No token at all: signed out, acting as nobody, with no answer of the last person left in the cache.
function forget(): void {
  setAccessToken(null);
  actingAs.value = null;
  signedIn.value = false;
}

type ExitAnswer = {
  data?: RefreshEnvelope;
  error?: object;
  response: Response;
};

// Exit from inside an act-as session: the bot ends the support session and hands back the admin's own session.
// Without a token in the answer the refresh cookie brings it; without that either, the admin is signed out.
export async function exitActAs(): Promise<void> {
  const answer: ExitAnswer = await client.POST('/api/v{version}/auth/impersonation/exit', {
    params: version,
    credentials: 'include',
  });

  // The session had already ended: the 401 retry brought the admin's own session back on the way.
  if (actingAs.value === null) return;

  if (answer.error !== undefined)
    throw problemError('exit act as', answer.response.status, answer.error);

  const token = answer.data?.data?.accessToken;

  if (typeof token === 'string') {
    acceptAccessToken(token);

    return;
  }

  if (await refresh()) return;

  forget();
  queryClient.clear();
}

// A sign-in and its answers belong to one bot; another bot starts signed out with an empty cache.
export function switchBot(origin: string): void {
  if (origin === activeBot.value) return;

  forget();
  queryClient.clear();
  activeBot.value = origin;
}

export async function signOut(): Promise<void> {
  await client.POST('/api/v{version}/auth/logout', { params: version, credentials: 'include' });
  forget();
  queryClient.clear();
}
