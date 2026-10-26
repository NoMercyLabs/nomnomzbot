// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useMutation, useQueryClient, type UseQueryReturnType } from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// The outside hosts the channel's webhooks may post to. A host that is not on the list, or is switched off, is
// refused by the bot before any call leaves.
export type AllowedHost = components['schemas']['HttpEgressAllowlist'];
export type AllowedHostBlastRadius = components['schemas']['BlastRadius'];

const version = '1';
const pageSize = 100;

export const egressAllowlistKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'egress-allowlist',
];

async function readHosts(channelId: string): Promise<AllowedHost[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/egress-allowlist',
    { params: { path: { version, channelId }, query: { Page: 1, PageSize: pageSize } } },
  );

  if (error !== undefined)
    throw failed('allowed hosts', response.status, error);

  return [...(data?.data ?? [])];
}

export function useAllowedHosts(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<AllowedHost[], Error> {
  return channelQuery(channelId, egressAllowlistKey, readHosts);
}

// Every write ends the same way: the list is read again, so the page shows what the bot now holds.
function useRefreshHosts(channelId: MaybeRefOrGetter<string | null>): () => Promise<void> {
  const queries = useQueryClient();

  return () => queries.invalidateQueries({ queryKey: egressAllowlistKey(toValue(channelId)) });
}

export function useAddAllowedHost(channelId: MaybeRefOrGetter<string | null>) {
  return useMutation({
    mutationFn: async (host: string): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/egress-allowlist',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { host },
        },
      );

      if (error !== undefined)
        throw refusal('add allowed host', error, response.status);
    },
    onSuccess: useRefreshHosts(channelId),
  });
}

export type AllowedHostSwitch = { host: AllowedHost; isEnabled: boolean };

export function useSwitchAllowedHost(channelId: MaybeRefOrGetter<string | null>) {
  return useMutation({
    mutationFn: async ({ host, isEnabled }: AllowedHostSwitch): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/egress-allowlist/{allowlistId}/enabled',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              allowlistId: host.id,
            },
          },
          body: { isEnabled },
        },
      );

      if (error !== undefined)
        throw refusal('switch allowed host', error, response.status);
    },
    onSuccess: useRefreshHosts(channelId),
  });
}

// The bot counts what relies on a host before the confirm opens: a host whose reach cannot be read is not offered
// for removal.
export async function fetchAllowedHostBlastRadius(
  channelId: string,
  host: AllowedHost,
): Promise<AllowedHostBlastRadius> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/egress-allowlist/{allowlistId}/blast-radius',
    { params: { path: {
      version,
      channelId,
      allowlistId: host.id,
    } } },
  );

  if (error !== undefined || !data?.data)
    throw failed('allowed host blast radius', response.status, error);

  return data.data;
}

export function useRemoveAllowedHost(channelId: MaybeRefOrGetter<string | null>) {
  return useMutation({
    mutationFn: async (host: AllowedHost): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/egress-allowlist/{allowlistId}',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              allowlistId: host.id,
            },
          },
        },
      );

      if (error !== undefined)
        throw failed('remove allowed host', response.status, error);
    },
    onSuccess: useRefreshHosts(channelId),
  });
}
