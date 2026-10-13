// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useQuery, useQueryClient, type QueryClient, type UseMutationReturnType, type UseQueryReturnType } from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'

import { useOptimisticSave } from '@/composables/useOptimisticSave'

import { client } from './client'
import type { components } from './schema'

export type MusicQueue = components['schemas']['MusicQueueDto']
export type QueueItem = components['schemas']['QueueItemDto']
export type NowPlaying = components['schemas']['NowPlayingDto']
export type MusicConfig = components['schemas']['MusicConfigDto']
export type MusicConfigChange = components['schemas']['UpdateMusicConfigDto']
export type BlockedTrack = components['schemas']['BlockedTrackDto']
export type BlockTrack = components['schemas']['BlockTrackRequest']

const version = '1'
const blockedPageSize = 100

// The realtime layer writes into the queue entry, so the keys are part of the module's surface.
export const musicQueueKey = (channelId: string | null): unknown[] => ['channels', channelId, 'music-queue']
export const musicConfigKey = (channelId: string | null): unknown[] => ['channels', channelId, 'music-config']
export const blockedTracksKey = (channelId: string | null): unknown[] => ['channels', channelId, 'music-blocked-tracks']

function failed(what: string, status: number): Error {
  return new Error(`${what} ${status}`)
}

function channelQuery<T>(
  channelId: MaybeRefOrGetter<string | null>,
  key: (channelId: string | null) => unknown[],
  fetch: (channelId: string) => Promise<T>,
): UseQueryReturnType<T, Error> {
  return useQuery({
    queryKey: computed(() => key(toValue(channelId))),
    queryFn: () => fetch(toValue(channelId) as string),
    enabled: computed(() => toValue(channelId) !== null),
  })
}

async function readQueue(channelId: string): Promise<MusicQueue> {
  const { data, error, response } = await client.GET('/api/v{version}/channels/{channelId}/music/queue', {
    params: { path: { version, channelId } },
  })
  if (error !== undefined) throw failed('music queue', response.status)
  return data?.data ?? { nowPlaying: null, queue: [] }
}

async function readBlocked(channelId: string): Promise<BlockedTrack[]> {
  const { data, error, response } = await client.GET('/api/v{version}/channels/{channelId}/music/blocked-tracks', {
    params: { path: { version, channelId }, query: { PageSize: blockedPageSize } },
  })
  if (error !== undefined) throw failed('blocked tracks', response.status)
  return data?.data ?? []
}

export function useMusicQueue(channelId: MaybeRefOrGetter<string | null>): UseQueryReturnType<MusicQueue, Error> {
  return channelQuery(channelId, musicQueueKey, readQueue)
}

export function useMusicConfig(channelId: MaybeRefOrGetter<string | null>): UseQueryReturnType<MusicConfig | null, Error> {
  return channelQuery(channelId, musicConfigKey, async (id: string): Promise<MusicConfig | null> => {
    const { data, error, response } = await client.GET('/api/v{version}/channels/{channelId}/music/config', {
      params: { path: { version, channelId: id } },
    })
    if (error !== undefined) throw failed('music config', response.status)
    return data?.data ?? null
  })
}

export function useBlockedTracks(channelId: MaybeRefOrGetter<string | null>): UseQueryReturnType<BlockedTrack[], Error> {
  return channelQuery(channelId, blockedTracksKey, readBlocked)
}

type QueueMutation = UseMutationReturnType<MusicQueue, Error, void, number>
type RowMutation = UseMutationReturnType<MusicQueue, Error, { position: number }, number>

// Rows keep the position numbers the bot handed out, in order, so the next call names the row on screen.
function renumbered(original: QueueItem[], rows: QueueItem[]): QueueItem[] {
  return rows.map((row: QueueItem, index: number): QueueItem => ({ ...row, position: original[index]?.position ?? row.position }))
}

function withPlaying(current: MusicQueue | undefined, isPlaying: boolean): MusicQueue {
  const base: MusicQueue = current ?? { nowPlaying: null, queue: [] }
  return { ...base, nowPlaying: base.nowPlaying === null ? null : { ...base.nowPlaying, isPlaying } }
}

function transport(channelId: MaybeRefOrGetter<string | null>, path: '/api/v{version}/channels/{channelId}/music/pause' | '/api/v{version}/channels/{channelId}/music/resume' | '/api/v{version}/channels/{channelId}/music/skip', isPlaying: boolean | null): QueueMutation {
  return useOptimisticSave<MusicQueue, void>({
    queryKey: () => musicQueueKey(toValue(channelId)),
    mutationFn: async (): Promise<MusicQueue> => {
      const id: string = toValue(channelId) as string
      const { error, response } = await client.POST(path, { params: { path: { version, channelId: id } } })
      if (error !== undefined) throw failed('music control', response.status)
      return readQueue(id)
    },
    apply: (current: MusicQueue | undefined): MusicQueue =>
      isPlaying === null ? (current ?? { nowPlaying: null, queue: [] }) : withPlaying(current, isPlaying),
  })
}

export function usePauseMusic(channelId: MaybeRefOrGetter<string | null>): QueueMutation {
  return transport(channelId, '/api/v{version}/channels/{channelId}/music/pause', false)
}

export function useResumeMusic(channelId: MaybeRefOrGetter<string | null>): QueueMutation {
  return transport(channelId, '/api/v{version}/channels/{channelId}/music/resume', true)
}

// A skip changes the track only when the bot answers, so nothing moves before then.
export function useSkipMusic(channelId: MaybeRefOrGetter<string | null>): QueueMutation {
  return transport(channelId, '/api/v{version}/channels/{channelId}/music/skip', null)
}

export function usePromoteQueueItem(channelId: MaybeRefOrGetter<string | null>): RowMutation {
  return useOptimisticSave<MusicQueue, { position: number }>({
    queryKey: () => musicQueueKey(toValue(channelId)),
    mutationFn: async ({ position }: { position: number }): Promise<MusicQueue> => {
      const id: string = toValue(channelId) as string
      const { error, response } = await client.POST('/api/v{version}/channels/{channelId}/music/queue/{position}/promote', {
        params: { path: { version, channelId: id, position } },
      })
      if (error !== undefined) throw failed('promote', response.status)
      return readQueue(id)
    },
    apply: (current: MusicQueue | undefined, { position }: { position: number }): MusicQueue => {
      const base: MusicQueue = current ?? { nowPlaying: null, queue: [] }
      const moved: QueueItem | undefined = base.queue.find((item: QueueItem) => item.position === position)
      if (moved === undefined) return base
      const rest: QueueItem[] = base.queue.filter((item: QueueItem) => item.position !== position)
      return { ...base, queue: renumbered(base.queue, [moved, ...rest]) }
    },
  })
}

function leavesQueue(current: MusicQueue | undefined, position: number): MusicQueue {
  const base: MusicQueue = current ?? { nowPlaying: null, queue: [] }
  const rest: QueueItem[] = base.queue.filter((item: QueueItem) => item.position !== position)
  return { ...base, queue: renumbered(base.queue, rest) }
}

// A removed song leaves the queue at once; a refused remove puts it back and reaches the error inbox.
export function useRemoveQueueItem(channelId: MaybeRefOrGetter<string | null>): RowMutation {
  return useOptimisticSave<MusicQueue, { position: number }>({
    queryKey: () => musicQueueKey(toValue(channelId)),
    mutationFn: async ({ position }: { position: number }): Promise<MusicQueue> => {
      const id: string = toValue(channelId) as string
      const { error, response } = await client.DELETE('/api/v{version}/channels/{channelId}/music/queue/{position}', {
        params: { path: { version, channelId: id, position } },
      })
      if (error !== undefined) throw failed('remove song', response.status)
      return readQueue(id)
    },
    apply: (current: MusicQueue | undefined, { position }: { position: number }): MusicQueue => leavesQueue(current, position),
  })
}

// Banning a queued song takes it out of the queue and adds it to the blocked list.
export function useBanQueueItem(channelId: MaybeRefOrGetter<string | null>): RowMutation {
  const queries: QueryClient = useQueryClient()
  return useOptimisticSave<MusicQueue, { position: number }>({
    queryKey: () => musicQueueKey(toValue(channelId)),
    mutationFn: async ({ position }: { position: number }): Promise<MusicQueue> => {
      const id: string = toValue(channelId) as string
      const { error, response } = await client.POST('/api/v{version}/channels/{channelId}/music/queue/{position}/ban', {
        params: { path: { version, channelId: id, position } },
      })
      if (error !== undefined) throw failed('ban song', response.status)
      await queries.invalidateQueries({ queryKey: blockedTracksKey(id) })
      return readQueue(id)
    },
    apply: (current: MusicQueue | undefined, { position }: { position: number }): MusicQueue => leavesQueue(current, position),
  })
}

export function useBlockTrack(channelId: MaybeRefOrGetter<string | null>): UseMutationReturnType<BlockedTrack[], Error, BlockTrack, number> {
  return useOptimisticSave<BlockedTrack[], BlockTrack>({
    queryKey: () => blockedTracksKey(toValue(channelId)),
    mutationFn: async (track: BlockTrack): Promise<BlockedTrack[]> => {
      const id: string = toValue(channelId) as string
      const { error, response } = await client.POST('/api/v{version}/channels/{channelId}/music/blocked-tracks', {
        params: { path: { version, channelId: id } },
        body: track,
      })
      if (error !== undefined) throw failed('block song', response.status)
      return readBlocked(id)
    },
    apply: (current: BlockedTrack[] | undefined, track: BlockTrack): BlockedTrack[] => [
      {
        id: `pending:${track.trackUri}`,
        provider: track.provider,
        trackUri: track.trackUri,
        title: track.title,
        reason: track.reason ?? null,
        blockedByUserId: track.blockedByUserId ?? null,
        createdAt: new Date().toISOString(),
      },
      ...(current ?? []),
    ],
  })
}

export function useUnblockTrack(channelId: MaybeRefOrGetter<string | null>): UseMutationReturnType<BlockedTrack[], Error, { blockedTrackId: string }, number> {
  return useOptimisticSave<BlockedTrack[], { blockedTrackId: string }>({
    queryKey: () => blockedTracksKey(toValue(channelId)),
    mutationFn: async ({ blockedTrackId }: { blockedTrackId: string }): Promise<BlockedTrack[]> => {
      const id: string = toValue(channelId) as string
      const { error, response } = await client.DELETE('/api/v{version}/channels/{channelId}/music/blocked-tracks/{blockedTrackId}', {
        params: { path: { version, channelId: id, blockedTrackId } },
      })
      if (error !== undefined) throw failed('unblock song', response.status)
      return readBlocked(id)
    },
    apply: (current: BlockedTrack[] | undefined, { blockedTrackId }: { blockedTrackId: string }): BlockedTrack[] =>
      (current ?? []).filter((track: BlockedTrack) => track.id !== blockedTrackId),
  })
}

function definedOnly(change: MusicConfigChange): Partial<MusicConfig> {
  return Object.fromEntries(Object.entries(change).filter(([, value]) => value !== undefined && value !== null)) as Partial<MusicConfig>
}

// Only the changed fields travel in the PUT; the cache shows them at once and then holds what the bot returns.
export function useSaveMusicConfig(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<MusicConfig | null, Error, MusicConfigChange, number> {
  return useOptimisticSave<MusicConfig | null, MusicConfigChange>({
    queryKey: () => musicConfigKey(toValue(channelId)),
    mutationFn: async (change: MusicConfigChange): Promise<MusicConfig | null> => {
      const id: string = toValue(channelId) as string
      const { data, error, response } = await client.PUT('/api/v{version}/channels/{channelId}/music/config', {
        params: { path: { version, channelId: id } },
        body: change,
      })
      if (error !== undefined) throw failed('save music config', response.status)
      return data?.data ?? null
    },
    apply: (current: MusicConfig | null | undefined, change: MusicConfigChange): MusicConfig | null =>
      current ? { ...current, ...definedOnly(change) } : null,
  })
}
