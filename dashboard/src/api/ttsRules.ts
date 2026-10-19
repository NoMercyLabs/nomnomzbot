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
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { useOptimisticSave } from '@/composables/useOptimisticSave';
import type { KeyParts } from '@/lib/array';

import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import type { components } from './schema';
import { ttsConfigKey, type TtsConfig } from './tts';

export type TtsLexiconEntry = components['schemas']['TtsLexiconEntry'];
export type TtsLexiconDraft = components['schemas']['UpsertTtsLexiconEntry'];
export type NamePronunciation = components['schemas']['ChannelNamePronunciation'];

// The rule toggles and the bits gate of the Rules tab. minBitsToTts 0 clears the gate.
export type TtsRulesChange = {
  isEnabled?: boolean;
  mode?: string;
  defaultProvider?: string;
  maxCharacters?: number;
  minPermission?: string;
  skipBotMessages?: boolean;
  profanityCensorEnabled?: boolean;
  followPlatformDefaultVoice?: boolean;
  readUsernames?: boolean;
  modApprovalRequired?: boolean;
  viewerVoiceSelfServiceEnabled?: boolean;
  minBitsToTts?: number;
};

// The providers that take a channel's own key.
export const byokProviders = ['azure', 'elevenlabs'] as const;
export type ByokProvider = (typeof byokProviders)[number];

export type ByokKey = {
  provider: ByokProvider;
  apiKey: string;
  region?: string;
};

const version = '1';

export const lexiconKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'tts-lexicon',
];

export const ttsDefaultsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'tts-defaults',
];

export const namePronunciationKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'tts-name-pronunciation',
];

// The config as it looks once the change is accepted. The bot names the follow flag differently on read.
function applyRules(current: TtsConfig, change: TtsRulesChange) {
  const { followPlatformDefaultVoice, minBitsToTts, ...flags } = change;

  return {
    ...current,
    ...flags,
    followsPlatformDefaultVoice: followPlatformDefaultVoice ?? current.followsPlatformDefaultVoice,
    minBitsToTts: minBitsToTts === undefined
      ? current.minBitsToTts
      : minBitsToTts || null,
  } as const satisfies TtsConfig;
}

export function useSetTtsRules(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<TtsConfig | null, Error, TtsRulesChange, number> {
  return useOptimisticSave<TtsConfig | null, TtsRulesChange>({
    describe: () => ({ key: 'setTtsRules', params: {} }),
    queryKey: () => ttsConfigKey(toValue(channelId)),
    mutationFn: async (change: TtsRulesChange): Promise<TtsConfig | null> => {
      const { data, error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/tts/config',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: change,
        },
      );

      if (error !== undefined)
        throw refusal('tts config', error, response.status);

      return data?.data ?? null;
    },
    apply: (current: TtsConfig | null | undefined, change: TtsRulesChange): TtsConfig | null =>
      current
        ? applyRules(current, change)
        : null,
  });
}

async function readTtsDefaults(channelId: string): Promise<TtsConfig | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/tts/config/defaults',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined)
    throw failed('tts config', response.status, error);

  return data?.data ?? null;
}

// The values the settings return to on a reset, so each control can name its default.
export function useTtsDefaults(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<TtsConfig | null, Error> {
  return channelQuery(channelId, ttsDefaultsKey, readTtsDefaults);
}

// Keys, the channel voice, the pronunciation rules and viewer voices stay as they are.
export function useResetTtsRules(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation<void, Error, void>({
    mutationFn: async (): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/tts/config/reset',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw refusal('tts config', error, response.status);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: ttsConfigKey(toValue(channelId)) }),
  });
}

async function readLexicon(channelId: string): Promise<TtsLexiconEntry[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/tts/lexicon',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined)
    throw failed('tts lexicon', response.status, error);

  return data?.data ?? [];
}

export function useTtsLexicon(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<TtsLexiconEntry[], Error> {
  return channelQuery(channelId, lexiconKey, readLexicon);
}

// entryId null adds a rule; an id replaces that rule.
export type LexiconSave = { entryId: string | null; draft: TtsLexiconDraft };

async function writeLexicon(channelId: string, { entryId, draft }: LexiconSave): Promise<void> {
  const path = { version, channelId };
  const { error, response } =
    entryId === null
      ? await client.POST('/api/v{version}/channels/{channelId}/tts/lexicon', {
          params: { path },
          body: draft,
        })
      : await client.PUT('/api/v{version}/channels/{channelId}/tts/lexicon/{entryId}', {
          params: { path: { ...path, entryId } },
          body: draft,
        });

  if (error !== undefined)
    throw refusal('tts lexicon', error, response.status);
}

export function useSaveLexiconEntry(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation<void, Error, LexiconSave>({
    mutationFn: (save: LexiconSave) => writeLexicon(toValue(channelId) as string, save),
    onSuccess: () => queries.invalidateQueries({ queryKey: lexiconKey(toValue(channelId)) }),
  });
}

export function useDeleteLexiconEntry(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation<void, Error, string>({
    mutationFn: async (entryId: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/tts/lexicon/{entryId}',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              entryId,
            },
          },
        },
      );

      if (error !== undefined)
        throw refusal('tts lexicon', error, response.status);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: lexiconKey(toValue(channelId)) }),
  });
}

// The key goes to the bot and is never read back: the config only says whether one is stored.
export function useSetByokKey(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation<void, Error, ByokKey>({
    mutationFn: async ({ provider, apiKey, region }: ByokKey): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/tts/config/byok/{provider}',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              provider,
            },
          },
          body: region
            ? { apiKey, region }
            : { apiKey },
        },
      );

      if (error !== undefined)
        throw refusal('tts key', error, response.status);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: ttsConfigKey(toValue(channelId)) }),
  });
}

export function useRemoveByokKey(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation<void, Error, ByokProvider>({
    mutationFn: async (provider: ByokProvider): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/tts/config/byok/{provider}',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              provider,
            },
          },
        },
      );

      if (error !== undefined)
        throw refusal('tts key', error, response.status);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: ttsConfigKey(toValue(channelId)) }),
  });
}

async function readNamePronunciation(channelId: string): Promise<NamePronunciation | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/tts/name-pronunciation',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined)
    throw failed('name pronunciation', response.status, error);

  return data?.data ?? null;
}

export function useNamePronunciation(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<NamePronunciation | null, Error> {
  return channelQuery(channelId, namePronunciationKey, readNamePronunciation);
}

// An empty text clears the spelling, and the bot then reads the channel name as it is.
export function useSetNamePronunciation(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation<NamePronunciation | null, Error, string>({
    mutationFn: async (pronunciation: string): Promise<NamePronunciation | null> => {
      const { data, error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/tts/name-pronunciation',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { pronunciation },
        },
      );

      if (error !== undefined)
        throw refusal('name pronunciation', error, response.status);

      return data?.data ?? null;
    },
    onSuccess: (saved: NamePronunciation | null) => {
      const key = namePronunciationKey(toValue(channelId));

      queries.setQueryData(key, saved);
    },
  });
}
