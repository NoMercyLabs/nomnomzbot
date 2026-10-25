// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import {
  useMutation,
  useQueryClient,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelOptions, channelQuery, refusal } from './channelQuery';
import { client } from './client';
import { failed } from './errors';
import type { components } from './schema';

export type SupporterEvent = components['schemas']['SupporterEvent'];

const version = '1';
const pageSize = 50;

export const supporterEventsKey = (channelId: string | null): KeyParts => [
  'supporters',
  channelId,
  'events',
];

// Tips, memberships, merch and charity from the connected providers, newest first.
export function useSupporterEvents(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<SupporterEvent[], Error> {
  return channelQuery(channelId, supporterEventsKey, readSupporterEvents);
}

export const supporterEventsOptions = (id: string) =>
  channelOptions(supporterEventsKey, readSupporterEvents, id);

async function readSupporterEvents(): Promise<SupporterEvent[]> {
  const { data, error, response } = await client.GET('/api/v{version}/supporters/events', {
    params: {
      path: { version },
      query: { PageSize: pageSize },
    },
  });

  if (error !== undefined)
    throw failed('supporter events', response.status, error);

  return data?.data ?? [];
}

export type SupporterConnection = components['schemas']['SupporterConnection'];
export type SupporterConnectionDraft = components['schemas']['UpsertSupporterConnectionRequest'];

// The providers the bot can take tips or memberships from, with how each one delivers events. The bot
// refuses a mode that differs from the provider's own, so the form never offers a choice.
export const supporterSources = {
  buymeacoffee: { mode: 'webhook', oauth: false },
  donordrive: { mode: 'poll', oauth: false },
  fourthwall: { mode: 'webhook', oauth: false },
  kofi: { mode: 'webhook', oauth: false },
  pally: { mode: 'ws', oauth: false },
  patreon: { mode: 'webhook', oauth: true },
  shopify: { mode: 'webhook', oauth: true },
  streamelements: { mode: 'socket', oauth: false },
  streamlabs: { mode: 'socket', oauth: false },
  tipeee: { mode: 'socket', oauth: false },
  treatstream: { mode: 'socket', oauth: true },
} as const satisfies Record<string, { mode: string; oauth: boolean }>;

export type SupporterSourceKey = keyof typeof supporterSources;

export const supporterConnectionsKey = (channelId: string | null): KeyParts => [
  'supporters',
  channelId,
  'connections',
];

async function readSupporterConnections(): Promise<SupporterConnection[]> {
  const { data, error, response } = await client.GET('/api/v{version}/supporters/connections', {
    params: { path: { version } },
  });

  if (error !== undefined)
    throw failed('supporter connections', response.status, error);

  return data?.data ?? [];
}

export function useSupporterConnections(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<SupporterConnection[], Error> {
  return channelQuery(channelId, supporterConnectionsKey, readSupporterConnections);
}

// Removes one source's connection with its stored secret; the source can be set up again later.
export function useDeleteSupporterConnection(
  channelId: MaybeRefOrGetter<string | null>,
) {
  const queries = useQueryClient();

  return useMutation<void, Error, SupporterSourceKey>({
    mutationFn: async (sourceKey: SupporterSourceKey): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/supporters/connections/{sourceKey}',
        { params: { path: { version, sourceKey } } },
      );

      if (error !== undefined)
        throw refusal('supporter connection', error, response.status);
    },
    onSuccess: () =>
      queries.invalidateQueries({ queryKey: supporterConnectionsKey(toValue(channelId)) }),
  });
}

// The secret goes to the bot and is never read back: a connection only says whether one is stored.
export function useSaveSupporterConnection(
  channelId: MaybeRefOrGetter<string | null>,
) {
  const queries = useQueryClient();

  return useMutation<void, Error, SupporterConnectionDraft>({
    mutationFn: async (draft: SupporterConnectionDraft): Promise<void> => {
      const { error, response } = await client.PUT('/api/v{version}/supporters/connections', {
        params: { path: { version } },
        body: draft,
      });

      if (error !== undefined)
        throw refusal('supporter connection', error, response.status);
    },
    onSuccess: () =>
      queries.invalidateQueries({ queryKey: supporterConnectionsKey(toValue(channelId)) }),
  });
}
