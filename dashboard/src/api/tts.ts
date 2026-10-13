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
  useInfiniteQuery,
  useMutation,
  type InfiniteData,
  type UseInfiniteQueryReturnType,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'

import { useOptimisticSave } from '@/composables/useOptimisticSave'

import { channelQuery, failed, refusal } from './channelQuery'
import { client } from './client'
import type { components } from './schema'

export type TtsConfig = components['schemas']['TtsConfigDto']
export type TtsOverlay = components['schemas']['TtsOverlayDto']
export type TtsVoice = components['schemas']['TtsVoiceDto']
export type TtsSampleResult = components['schemas']['TtsTestResultDto']
export type TtsVoiceFilters = { q: string }
export type TtsVoicePage = {
  voices: TtsVoice[]
  hasMore: boolean
  nextPage: number | null
  total: number | null
}
export type TtsVoicePick = { voiceId: string; provider: string }
export type TtsSampleRequest = { voiceId: string; text: string }

const version = '1'
// The server clamps a voices page to 200 rows.
const voicePageSize = 200
const firstVoicePage = 1
const languageFamilies: readonly string[] = ['en', 'nl', 'de', 'fr', 'es']
// The order the voice list and its chips show the language families in.
export const voiceFamilyOrder: readonly string[] = [...languageFamilies, 'other']

// The realtime layer writes into these entries, so the keys are part of the module's surface.
export const ttsConfigKey = (channelId: string | null): unknown[] => [
  'channels',
  channelId,
  'tts-config',
]
export const ttsOverlayKey = (channelId: string | null): unknown[] => [
  'channels',
  channelId,
  'tts-overlay',
]
export const ttsVoicesKey = (channelId: string | null, filters: TtsVoiceFilters): unknown[] => [
  'channels',
  channelId,
  'tts-voices',
  filters,
]

// The language part of a locale such as en-US or nl_BE; a language the page has no chip for is "other".
export function voiceFamily(locale: string): string {
  const language: string = locale.split(/[-_]/)[0]?.toLowerCase() ?? ''
  return languageFamilies.includes(language) ? language : 'other'
}

export function useTtsConfig(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<TtsConfig | null, Error> {
  return channelQuery(channelId, ttsConfigKey, async (id: string): Promise<TtsConfig | null> => {
    const { data, error, response } = await client.GET(
      '/api/v{version}/channels/{channelId}/tts/config',
      {
        params: { path: { version, channelId: id } },
      },
    )
    if (error !== undefined) throw failed('tts config', response.status)
    return data?.data ?? null
  })
}

export function useTtsOverlay(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<TtsOverlay | null, Error> {
  return channelQuery(channelId, ttsOverlayKey, async (id: string): Promise<TtsOverlay | null> => {
    const { data, error, response } = await client.GET(
      '/api/v{version}/channels/{channelId}/tts/overlay',
      {
        params: { path: { version, channelId: id } },
      },
    )
    if (error !== undefined) throw failed('tts overlay', response.status)
    return data?.data ?? null
  })
}

async function readVoicePage(channelId: string, q: string, page: number): Promise<TtsVoicePage> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/tts/voices',
    {
      params: {
        path: { version, channelId },
        query: q === '' ? { page, pageSize: voicePageSize } : { q, page, pageSize: voicePageSize },
      },
    },
  )
  if (error !== undefined) throw failed('tts voices', response.status)
  const nextPage: number = Number(data?.nextPage)
  const total: number = Number(data?.total ?? Number.NaN)
  return {
    voices: data?.data ?? [],
    hasMore: data?.hasMore ?? false,
    nextPage: Number.isFinite(nextPage) ? nextPage : null,
    total: Number.isFinite(total) ? total : null,
  }
}

// The catalogue can hold thousands of voices; pages are appended while the bot says there are more.
export function useTtsVoices(
  channelId: MaybeRefOrGetter<string | null>,
  filters: MaybeRefOrGetter<TtsVoiceFilters>,
): UseInfiniteQueryReturnType<InfiniteData<TtsVoicePage, number>, Error> {
  return useInfiniteQuery({
    queryKey: computed(() => ttsVoicesKey(toValue(channelId), toValue(filters))),
    queryFn: ({ pageParam }: { pageParam: number }): Promise<TtsVoicePage> =>
      readVoicePage(toValue(channelId) as string, toValue(filters).q.trim(), pageParam),
    initialPageParam: firstVoicePage,
    getNextPageParam: (last: TtsVoicePage, all: TtsVoicePage[]): number | undefined =>
      last.hasMore ? (last.nextPage ?? all.length + 1) : undefined,
    enabled: computed(() => toValue(channelId) !== null),
  })
}

// A picked voice shows as the channel voice at once and rolls back when the bot refuses it.
export function useSetTtsVoice(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<TtsConfig | null, Error, TtsVoicePick, number> {
  return useOptimisticSave<TtsConfig | null, TtsVoicePick>({
    queryKey: () => ttsConfigKey(toValue(channelId)),
    mutationFn: async ({ voiceId, provider }: TtsVoicePick): Promise<TtsConfig | null> => {
      const { data, error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/tts/config',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: {
            defaultVoiceId: voiceId,
            defaultProvider: provider,
            followPlatformDefaultVoice: false,
          },
        },
      )
      if (error !== undefined) throw refusal('tts config', error, response.status)
      return data?.data ?? null
    },
    apply: (
      current: TtsConfig | null | undefined,
      { voiceId, provider }: TtsVoicePick,
    ): TtsConfig | null =>
      current
        ? {
            ...current,
            defaultVoiceId: voiceId,
            defaultProvider: provider,
            followsPlatformDefaultVoice: false,
          }
        : null,
  })
}

export function useTtsSample(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<TtsSampleResult, Error, TtsSampleRequest, unknown> {
  return useMutation<TtsSampleResult, Error, TtsSampleRequest>({
    mutationFn: async ({ voiceId, text }: TtsSampleRequest): Promise<TtsSampleResult> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/tts/test',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { text, voiceId },
        },
      )
      if (error !== undefined || !data?.data) throw refusal('tts sample', error, response.status)
      return data.data
    },
  })
}
