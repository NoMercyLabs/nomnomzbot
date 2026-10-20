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
  useQuery,
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { useOptimisticSave } from '@/composables/useOptimisticSave';
import type { KeyParts } from '@/lib/array';

import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

// A person's own account: pronouns, linked sign-ins, standing and TTS voice. Every call acts on the signed-in
// person only (the bot reads the caller from the sign-in), so no call names another person.
export type PronounOption = components['schemas']['PronounCatalog'];
export type MyPronouns = components['schemas']['UserPronoun'];
export type MyStanding = components['schemas']['SelfStanding'];
export type MyVoice = components['schemas']['UserTtsVoice'];

// The bot's list sends no row id yet, which the unlink call needs: a row without one offers no unlink.
export type LinkedIdentity = components['schemas']['UserIdentity'] & { id?: string | null };

export type PronounChange = { pronounId?: number; altPronounId?: number };

const version = '1';

export const pronounCatalogKey = (): KeyParts => ['viewer', 'me', 'pronoun-catalog'];
export const myPronounsKey = (): KeyParts => ['viewer', 'me', 'pronouns'];
export const myIdentitiesKey = (): KeyParts => ['viewer', 'me', 'identities'];

export const myStandingKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'viewer',
  'standing',
];

export const myVoiceKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'viewer',
  'voice',
];

export function usePronounCatalog(): UseQueryReturnType<PronounOption[], Error> {
  return useQuery({
    queryKey: pronounCatalogKey(),
    queryFn: async (): Promise<PronounOption[]> => {
      const { data, error, response } = await client.GET('/api/v{version}/pronouns/catalog', {
        params: { path: { version } },
      });

      if (error !== undefined)
        throw failed('pronoun catalog', response.status, error);

      return [...(data?.data ?? [])];
    },
  });
}

export function useMyPronouns(): UseQueryReturnType<MyPronouns | null, Error> {
  return useQuery({
    queryKey: myPronounsKey(),
    queryFn: async (): Promise<MyPronouns | null> => {
      const { data, error, response } = await client.GET('/api/v{version}/pronouns/me', {
        params: { path: { version } },
      });

      if (error !== undefined)
        throw failed('my pronouns', response.status, error);

      return data?.data ?? null;
    },
  });
}

// An id of 0 clears a pronoun; leaving a field out leaves it as it is.
export function useSetMyPronouns(): UseMutationReturnType<
  MyPronouns | null,
  Error,
  PronounChange,
  number
> {
  return useOptimisticSave<MyPronouns | null, PronounChange>({
    describe: () => ({ key: 'setMyPronouns' }),
    queryKey: myPronounsKey(),
    mutationFn: async (change: PronounChange): Promise<MyPronouns | null> => {
      const { data, error, response } = await client.PUT('/api/v{version}/pronouns/me', {
        params: { path: { version } },
        body: change,
      });

      if (error !== undefined)
        throw refusal('set my pronouns', error, response.status);

      return data?.data ?? null;
    },
    apply: (current: MyPronouns | null | undefined, change: PronounChange): MyPronouns => ({
      manualOverride: true,
      ...current,
      ...(change.pronounId === undefined
        ? {}
        : { pronounId: change.pronounId === 0
            ? null
            : change.pronounId }),
      ...(change.altPronounId === undefined
        ? {}
        : { altPronounId: change.altPronounId === 0
            ? null
            : change.altPronounId }),
    }),
  });
}

export function useMyIdentities(): UseQueryReturnType<LinkedIdentity[], Error> {
  return useQuery({
    queryKey: myIdentitiesKey(),
    queryFn: async (): Promise<LinkedIdentity[]> => {
      const { data, error, response } = await client.GET('/api/v{version}/auth/identities', {
        params: { path: { version } },
      });

      if (error !== undefined)
        throw failed('my identities', response.status, error);

      return [...(data?.data ?? [])];
    },
  });
}

// The bot refuses the main sign-in and the last one; the page offers neither.
export function useUnlinkIdentity(): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (identityId: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/auth/identities/{identityId}',
        { params: { path: { version, identityId } } },
      );

      if (error !== undefined)
        throw refusal('unlink identity', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: myIdentitiesKey() });
    },
  });
}

export function useMyStanding(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<MyStanding, Error> {
  return channelQuery(channelId, myStandingKey, async (id: string): Promise<MyStanding> => {
    const { data, error, response } = await client.GET(
      '/api/v{version}/channels/{channelId}/community/me/standing',
      { params: { path: { version, channelId: id } } },
    );

    if (error !== undefined || !data?.data)
      throw failed('my standing', response.status, error);

    return data.data;
  });
}

// Null when the person uses the channel voice.
export function useMyVoice(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<MyVoice | null, Error> {
  return channelQuery(channelId, myVoiceKey, async (id: string): Promise<MyVoice | null> => {
    const { data, error, response } = await client.GET(
      '/api/v{version}/channels/{channelId}/tts/me/voice',
      { params: { path: { version, channelId: id } } },
    );

    if (error !== undefined)
      throw failed('my voice', response.status, error);

    return data?.data ?? null;
  });
}

// A voice id sets the person's own voice; null goes back to the channel voice. Only their own words use it.
export function useSetMyVoice(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<MyVoice | null, Error, string | null, number> {
  return useOptimisticSave<MyVoice | null, string | null>({
    describe: (voiceId) => ({ key: voiceId === null
      ? 'clearMyVoice'
      : 'setMyVoice' }),
    queryKey: () => myVoiceKey(toValue(channelId)),
    mutationFn: async (voiceId: string | null): Promise<MyVoice | null> => {
      const path = { version, channelId: toValue(channelId) as string };

      if (voiceId === null) {
        const { error, response } = await client.DELETE(
          '/api/v{version}/channels/{channelId}/tts/me/voice',
          { params: { path } },
        );

        if (error !== undefined)
          throw refusal('clear my voice', error, response.status);

        return null;
      }

      const { data, error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/tts/me/voice',
        { params: { path }, body: { voiceId } },
      );

      if (error !== undefined)
        throw refusal('set my voice', error, response.status);

      return data?.data ?? null;
    },
    apply: (current: MyVoice | null | undefined, voiceId: string | null): MyVoice | null =>
      voiceId === null
        ? null
        : { userId: current?.userId ?? '', voiceId },
  });
}
