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

import {
  useMutation,
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// A channel's trust tuning and the shipped defaults it starts from. The bot publishes no event when the
// policy is saved, so this tab refetches after its own save; a second open tab shows the old values until it reloads.
export type TrustPolicy = components['schemas']['TrustPolicy'];
export type TrustPolicyChange = components['schemas']['UpdateTrustPolicyRequest'];

const version = '1';

export const trustPolicyKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'trust',
  'policy',
];

export const trustPolicyDefaultsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'trust',
  'policy-defaults',
];

async function readPolicy(channelId: string): Promise<TrustPolicy> {
  const { data, error, response } = await client.GET('/api/v{version}/channels/{channelId}/trust/policy', {
    params: { path: { version, channelId } },
  });

  const found = data?.data;

  if (error !== undefined || found === undefined || found === null)
    throw failed('trust policy', response.status, error);

  return found;
}

async function readDefaults(channelId: string): Promise<TrustPolicy> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/trust/policy/defaults',
    { params: { path: { version, channelId } } },
  );

  const found = data?.data;

  if (error !== undefined || found === undefined || found === null)
    throw failed('trust policy defaults', response.status, error);

  return found;
}

export function useTrustPolicy(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<TrustPolicy, Error> {
  return channelQuery(channelId, trustPolicyKey, readPolicy);
}

export function useTrustPolicyDefaults(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<TrustPolicy, Error> {
  return channelQuery(channelId, trustPolicyDefaultsKey, readDefaults);
}

// A plain mutation, not an optimistic save: a refusal names the field it is about, and the row shows it.
export function useSaveTrustPolicy(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<TrustPolicy | null, Error, TrustPolicyChange, undefined> {
  const queries = useQueryClient();

  return useMutation<TrustPolicy | null, Error, TrustPolicyChange, undefined>({
    mutationFn: async (change: TrustPolicyChange): Promise<TrustPolicy | null> => {
      const { data, error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/trust/policy',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: change,
        },
      );

      if (error !== undefined)
        throw refusal('save trust policy', error, response.status);

      return data?.data ?? null;
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: trustPolicyKey(toValue(channelId)) });
    },
  });
}
