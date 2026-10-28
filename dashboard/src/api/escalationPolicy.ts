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

// A channel's escalation ladder. One read carries the policy, the defaults it starts from and the limits a
// save must keep. The bot publishes no event when the policy is saved, so this tab refetches after its own
// save; a second open tab shows the old values until it reloads.
export type EscalationPolicy = components['schemas']['ModerationEscalationPolicy'];
export type EscalationChange = components['schemas']['UpsertEscalationPolicyRequest'];

const version = '1';

export const escalationPolicyKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'moderation',
  'escalation',
];

async function readPolicy(channelId: string): Promise<EscalationPolicy> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/moderation/escalation',
    { params: { path: { version, channelId } } },
  );

  const found = data?.data;

  if (error !== undefined || found === undefined || found === null)
    throw failed('escalation policy', response.status, error);

  return found;
}

export function useEscalationPolicy(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<EscalationPolicy, Error> {
  return channelQuery(channelId, escalationPolicyKey, readPolicy);
}

// A plain mutation, not an optimistic save: a refusal names the field it is about, and the row shows it.
export function useSaveEscalationPolicy(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<EscalationPolicy | null, Error, EscalationChange, undefined> {
  const queries = useQueryClient();

  return useMutation<EscalationPolicy | null, Error, EscalationChange, undefined>({
    mutationFn: async (change: EscalationChange): Promise<EscalationPolicy | null> => {
      const { data, error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/moderation/escalation',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: change,
        },
      );

      if (error !== undefined)
        throw refusal('save escalation policy', error, response.status);

      return data?.data ?? null;
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: escalationPolicyKey(toValue(channelId)) });
    },
  });
}
