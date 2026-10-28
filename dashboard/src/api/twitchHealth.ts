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

import { queryOptions, useMutation, useQueryClient } from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import { failed } from './errors';
import type { components } from './schema';

export type ScopeDiagnostics = components['schemas']['TwitchScopeDiagnostics'];
export type MissingScopes = components['schemas']['MissingScopes'];
export type EventSubSubscription = components['schemas']['EventSubSubscription'];
export type ReconcileReport = components['schemas']['EventSubReconcileReport'];
export type ScopeRegrant = components['schemas']['ScopeRegrantStart'];

const version = { path: { version: '1' } };

export const twitchHealthKey = (channelId: string) => [
  'channels',
  channelId,
  'twitch-health',
] as const satisfies readonly string[];

async function fetchScopes(): Promise<ScopeDiagnostics> {
  const { data, error, response } = await client.GET('/api/v{version}/twitch/diagnostics/scopes', {
    params: version,
  });

  if (!response.ok || !data?.data)
    throw failed('twitch scopes', response.status, error);

  return data.data;
}

async function fetchMissingScopes(): Promise<MissingScopes> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/twitch/diagnostics/missing-scopes',
    { params: version },
  );

  if (!response.ok || !data?.data)
    throw failed('twitch missing scopes', response.status, error);

  return data.data;
}

async function fetchSubscriptions(channelId: string): Promise<EventSubSubscription[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/eventsub/subscriptions',
    { params: { path: { version: '1', channelId } } },
  );

  if (!response.ok)
    throw failed('eventsub reconcile', response.status, error);

  return data?.data ?? [];
}

export type EventFeedFacts = {
  scopes: ScopeDiagnostics;
  missing: MissingScopes;
  subscriptions: EventSubSubscription[];
};

// The three reads together, so the screen judges the event feed from one answer.
export const eventFeedFactsQuery = (channelId: string) =>
  queryOptions({
    queryKey: twitchHealthKey(channelId),
    queryFn: async (): Promise<EventFeedFacts> => {
      const [scopes, missing, subscriptions] = await Promise.all([
        fetchScopes(),
        fetchMissingScopes(),
        fetchSubscriptions(channelId),
      ]);

      return {
        scopes,
        missing,
        subscriptions,
      };
    },
    retry: false,
  });

export type EventFeedVerdict =
  | { state: 'ok'; total: number } |
  { state: 'missingScopes'; scopes: string[] } |
  {
    state: 'brokenSubscriptions';
    broken: number;
    total: number;
  } |
  { state: 'noSubscriptions' };

// Scopes first: a subscription cannot work without its scope, so a re-grant comes before a reconcile.
export function judgeEventFeed(facts: EventFeedFacts) {
  if (facts.missing.scopes.length > 0)
    return { state: 'missingScopes', scopes: facts.missing.scopes.map((m) => m.scope) } as const satisfies EventFeedVerdict;

  if (facts.subscriptions.length === 0)
    return { state: 'noSubscriptions' } as const satisfies EventFeedVerdict;

  const broken = facts.subscriptions.filter((s) => !s.enabled || s.lastError).length;

  return broken > 0
    ? {
      state: 'brokenSubscriptions',
      broken,
      total: facts.subscriptions.length,
    } as const satisfies EventFeedVerdict
    : { state: 'ok', total: facts.subscriptions.length } as const satisfies EventFeedVerdict;
}

// Fix for the subscriptions: the bot compares its registry with what Twitch holds and repairs the gap.
export function useReconcileEventSub(channelId: MaybeRefOrGetter<string>) {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (): Promise<ReconcileReport> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/eventsub/reconcile',
        { params: { path: { version: '1', channelId: toValue(channelId) } } },
      );

      if (!response.ok || !data?.data)
        throw failed('eventsub reconcile', response.status, error);

      return data.data;
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: twitchHealthKey(toValue(channelId)) }),
  });
}

// Fix for the scopes: Twitch shows a code the person approves; the grant then widens to the missing scopes.
export function useStartRegrant() {
  return useMutation({
    mutationFn: async (): Promise<ScopeRegrant> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/twitch/diagnostics/regrant',
        { params: version },
      );

      if (!response.ok || !data?.data)
        throw failed('twitch regrant', response.status, error);

      return data.data;
    },
  });
}
