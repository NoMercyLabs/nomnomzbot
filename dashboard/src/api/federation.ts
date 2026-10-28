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

// A federation opt-in is the channel's own yes to one kind of sharing with other bot instances. Nothing is
// shared or accepted until an enabled opt-in exists.
export type FederationOptIn = components['schemas']['ChannelFederationOptIn'];
export type FederationOptInDraft = components['schemas']['UpsertChannelFederationOptInRequest'];

// The kinds of data a channel can share or accept (FederationOptInType in the bot).
export const federationOptInTypes = [
  'shared_chat_bans',
  'shared_ban_list',
  'shared_trust_list',
  'shared_savings',
] as const;

// The way a choice runs (FederationDirection in the bot).
export const federationDirections = ['accept', 'share', 'both'] as const;

export type FederationOptInType = (typeof federationOptInTypes)[number];
export type FederationDirection = (typeof federationDirections)[number];

const version = '1';

export const federationOptInsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'federation-opt-ins',
];

async function readOptIns(channelId: string): Promise<FederationOptIn[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/federation/opt-ins',
    { params: { path: { version, channelId } } },
  );

  const rows = data?.data;

  if (error !== undefined || rows === undefined || rows === null)
    throw failed('federation opt-ins', response.status, error);

  return rows;
}

// The bot publishes FederationOptInChanged for every change (realtime/liveSync.ts).
export function useFederationOptIns(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<FederationOptIn[], Error> {
  return channelQuery(channelId, federationOptInsKey, readOptIns);
}

// The same type and peer replace the saved choice. No peer means any trusted peer.
export function useSaveFederationOptIn(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, FederationOptInDraft, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, FederationOptInDraft, undefined>({
    // The contract declares no body for the 200 of this PUT, so the list refetch is the source of the saved row.
    mutationFn: async (draft: FederationOptInDraft): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/federation/opt-ins',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: draft,
        },
      );

      if (error !== undefined)
        throw refusal('save federation opt-in', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: federationOptInsKey(toValue(channelId)) });
    },
  });
}

// Turning a choice off returns that flow to default-deny. The row can be switched on again later.
export function useDisableFederationOptIn(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (optInId: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/federation/opt-ins/{optInId}',
        { params: { path: {
          version,
          channelId: toValue(channelId) as string,
          optInId,
        } } },
      );

      if (error !== undefined)
        throw refusal('disable federation opt-in', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: federationOptInsKey(toValue(channelId)) });
    },
  });
}
