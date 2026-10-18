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
  useQuery,
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { useOptimisticSave } from '@/composables/useOptimisticSave';

import { channelOptions } from './channelQuery';
import { client } from './client';
import { failed } from './errors';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type MusicQueue = components['schemas']['MusicQueue'];
export type QueueItem = components['schemas']['QueueItem'];
export type NowPlaying = components['schemas']['NowPlaying'];
export type MusicConfig = components['schemas']['MusicConfig'];
export type MusicConfigChange = components['schemas']['UpdateMusicConfig'];
export type BlockedTrack = components['schemas']['BlockedTrack'];
export type BlockTrack = components['schemas']['BlockTrackRequest'];

const version = '1';
const blockedPageSize = 100;

// The realtime layer writes into the queue entry, so the keys are part of the module's surface.
export const musicQueueKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'music-queue',
];

export const musicConfigKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'music-config',
];

export const blockedTracksKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'music-blocked-tracks',
];

function channelQuery<T>(
  channelId: MaybeRefOrGetter<string | null>,
  key: (channelId: string | null) => KeyParts,
  fetch: (channelId: string) => Promise<T>,
): UseQueryReturnType<T, Error> {
  return useQuery({
    queryKey: computed(() => key(toValue(channelId))),
    queryFn: () => fetch(toValue(channelId) as string),
    enabled: computed(() => toValue(channelId) !== null),
  });
}

async function readQueue(channelId: string): Promise<MusicQueue> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/music/queue',
    {
      params: { path: { version, channelId } },
    },
  );

  if (error !== undefined)
    throw failed('music queue', response.status, error);

  return (
    data?.data ?? {
      nowPlaying: null,
      queue: [],
      requestsOpen: true,
    }
  );
}

async function readBlocked(channelId: string): Promise<BlockedTrack[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/music/blocked-tracks',
    {
      params: { path: { version, channelId }, query: { PageSize: blockedPageSize } },
    },
  );

  if (error !== undefined)
    throw failed('blocked tracks', response.status, error);

  return data?.data ?? [];
}

export function useMusicQueue(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<MusicQueue, Error> {
  return channelQuery(channelId, musicQueueKey, readQueue);
}

export function useMusicConfig(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<MusicConfig | null, Error> {
  return channelQuery(channelId, musicConfigKey, readConfig);
}

async function readConfig(id: string): Promise<MusicConfig | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/music/config',
    {
      params: { path: { version, channelId: id } },
    },
  );

  if (error !== undefined)
    throw failed('music config', response.status, error);

  return data?.data ?? null;
}

export const musicQueueOptions = (id: string) => channelOptions(musicQueueKey, readQueue, id);
export const musicConfigOptions = (id: string) => channelOptions(musicConfigKey, readConfig, id);

export const blockedTracksOptions = (id: string) =>
  channelOptions(blockedTracksKey, readBlocked, id);

export function useBlockedTracks(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<BlockedTrack[], Error> {
  return channelQuery(channelId, blockedTracksKey, readBlocked);
}

type QueueMutation = UseMutationReturnType<MusicQueue, Error, void, number>;
type RowMutation<TInput = { position: number }> = UseMutationReturnType<
  MusicQueue,
  Error,
  TInput,
  number
>;

// Rows keep the position numbers the bot handed out, in order, so the next call names the row on screen.
function renumbered(original: QueueItem[], rows: QueueItem[]): QueueItem[] {
  return rows.map((row, index) => ({
    ...row,
    position: original[index]?.position ?? row.position,
    number: original[index]?.number ?? row.number,
  }) as const satisfies QueueItem);
}

function withPlaying(current: MusicQueue | undefined, isPlaying: boolean): MusicQueue {
  const base: MusicQueue = current ?? {
    nowPlaying: null,
    queue: [],
    requestsOpen: true,
  };

  return {
    ...base,
    nowPlaying: base.nowPlaying === null
      ? null
      : { ...base.nowPlaying, isPlaying },
  };
}

function transportKey(isPlaying: boolean | null): 'skipSong' | 'resumeSong' | 'pauseSong' {
  if (isPlaying === null)
    return 'skipSong';

  if (isPlaying)
    return 'resumeSong';

  return 'pauseSong';
}

function transport(
  channelId: MaybeRefOrGetter<string | null>,
  path:
    | '/api/v{version}/channels/{channelId}/music/pause' |
    '/api/v{version}/channels/{channelId}/music/resume' |
    '/api/v{version}/channels/{channelId}/music/skip',
  isPlaying: boolean | null,
): QueueMutation {
  return useOptimisticSave<MusicQueue, void>({
    describe: () => ({ key: transportKey(isPlaying) }),
    queryKey: () => musicQueueKey(toValue(channelId)),
    mutationFn: async (): Promise<MusicQueue> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.POST(path, {
        params: { path: { version, channelId: id } },
      });

      if (error !== undefined)
        throw failed('music control', response.status, error);

      return readQueue(id);
    },
    apply: (current: MusicQueue | undefined): MusicQueue =>
      isPlaying === null
        ? (current ?? {
            nowPlaying: null,
            queue: [],
            requestsOpen: true,
          })
        : withPlaying(current, isPlaying),
  });
}

export function usePauseMusic(channelId: MaybeRefOrGetter<string | null>): QueueMutation {
  return transport(channelId, '/api/v{version}/channels/{channelId}/music/pause', false);
}

export function useResumeMusic(channelId: MaybeRefOrGetter<string | null>): QueueMutation {
  return transport(channelId, '/api/v{version}/channels/{channelId}/music/resume', true);
}

// Closing or opening the queue flips the badge at once; a refused change puts it back and reaches the error inbox.
export function useSetRequestsOpen(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<MusicQueue, Error, { open: boolean }, number> {
  return useOptimisticSave<MusicQueue, { open: boolean }>({
    describe: ({ open }) => ({ key: open
      ? 'openRequests'
      : 'closeRequests' }),
    queryKey: () => musicQueueKey(toValue(channelId)),
    mutationFn: async ({ open }: { open: boolean }): Promise<MusicQueue> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/music/queue/requests-open',
        {
          params: { path: { version, channelId: id } },
          body: { open },
        },
      );

      if (error !== undefined)
        throw failed('song requests', response.status, error);

      return readQueue(id);
    },
    apply: (current: MusicQueue | undefined, { open }: { open: boolean }): MusicQueue => ({
      ...(current ?? {
        nowPlaying: null,
        queue: [],
        requestsOpen: true,
      }),
      requestsOpen: open,
    }),
  });
}

// A skip changes the track only when the bot answers, so nothing moves before then.
export function useSkipMusic(channelId: MaybeRefOrGetter<string | null>): QueueMutation {
  return transport(channelId, '/api/v{version}/channels/{channelId}/music/skip', null);
}

export function usePromoteQueueItem(channelId: MaybeRefOrGetter<string | null>): RowMutation {
  return useOptimisticSave<MusicQueue, { position: number }>({
    describe: ({ position }) => ({
      key: 'promoteQueueItem',
      params: { position: String(position) },
    }),
    queryKey: () => musicQueueKey(toValue(channelId)),
    mutationFn: async ({ position }: { position: number }): Promise<MusicQueue> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/music/queue/{position}/promote',
        {
          params: {
            path: {
              version,
              channelId: id,
              position,
            },
          },
        },
      );

      if (error !== undefined)
        throw failed('promote', response.status, error);

      return readQueue(id);
    },
    apply: (current: MusicQueue | undefined, { position }: { position: number }): MusicQueue => {
      const base: MusicQueue = current ?? {
        nowPlaying: null,
        queue: [],
        requestsOpen: true,
      };
      const moved = base.queue.find((item) => item.position === position);

      if (moved === undefined)
        return base;

      const rest = base.queue.filter((item) => item.position !== position);

      return { ...base, queue: renumbered(base.queue, [moved, ...rest]) };
    },
  });
}

export type QueueMove = {
  position: number;
  toPosition: number;
};

// A dragged song lands exactly where it was dropped; the bot keeps the fair-queue rules for later requests.
export function useMoveQueueItem(
  channelId: MaybeRefOrGetter<string | null>,
): RowMutation<QueueMove> {
  return useOptimisticSave<MusicQueue, QueueMove>({
    describe: ({ position, toPosition }) => ({
      key: 'moveQueueItem',
      params: { position: String(position), toPosition: String(toPosition) },
    }),
    queryKey: () => musicQueueKey(toValue(channelId)),
    mutationFn: async ({ position, toPosition }: QueueMove): Promise<MusicQueue> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/music/queue/{position}/move',
        {
          params: {
            path: {
              version,
              channelId: id,
              position,
            },
          },
          body: { toPosition },
        },
      );

      if (error !== undefined)
        throw failed('move', response.status, error);

      return readQueue(id);
    },
    apply: (current: MusicQueue | undefined, { position, toPosition }: QueueMove): MusicQueue => {
      const base: MusicQueue = current ?? {
        nowPlaying: null,
        queue: [],
        requestsOpen: true,
      };
      const from = base.queue.findIndex((item) => item.position === position);
      const to = base.queue.findIndex((item) => item.position === toPosition);

      if (from < 0 || to < 0)
        return base;

      const rows = base.queue.filter((_, index) => index !== from);

      rows.splice(to, 0, base.queue[from]!);

      return { ...base, queue: renumbered(base.queue, rows) };
    },
  });
}

function leavesQueue(current: MusicQueue | undefined, position: number): MusicQueue {
  const base: MusicQueue = current ?? {
    nowPlaying: null,
    queue: [],
    requestsOpen: true,
  };
  const rest = base.queue.filter((item) => item.position !== position);

  return { ...base, queue: renumbered(base.queue, rest) };
}

// A removed song leaves the queue at once; a refused remove puts it back and reaches the error inbox.
export function useRemoveQueueItem(channelId: MaybeRefOrGetter<string | null>): RowMutation {
  return useOptimisticSave<MusicQueue, { position: number }>({
    describe: ({ position }) => ({
      key: 'removeQueueItem',
      params: { position: String(position) },
    }),
    queryKey: () => musicQueueKey(toValue(channelId)),
    mutationFn: async ({ position }: { position: number }): Promise<MusicQueue> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/music/queue/{position}',
        {
          params: {
            path: {
              version,
              channelId: id,
              position,
            },
          },
        },
      );

      if (error !== undefined)
        throw failed('remove song', response.status, error);

      return readQueue(id);
    },
    apply: (current: MusicQueue | undefined, { position }: { position: number }): MusicQueue =>
      leavesQueue(current, position),
  });
}

export type RequesterRemoval = { name: string };

function withoutRequester(current: MusicQueue | undefined, name: string): MusicQueue {
  const base: MusicQueue = current ?? {
    nowPlaying: null,
    queue: [],
    requestsOpen: true,
  };
  const rest = base.queue.filter(
    (item): boolean => item.requestedBy?.toLowerCase() !== name.toLowerCase(),
  );

  return { ...base, queue: renumbered(base.queue, rest) };
}

// Every queued song of one requester leaves at once; the playing track stays. A refusal puts them back.
export function useRemoveRequesterSongs(
  channelId: MaybeRefOrGetter<string | null>,
): RowMutation<RequesterRemoval> {
  return useOptimisticSave<MusicQueue, RequesterRemoval>({
    describe: ({ name }) => ({ key: 'removeRequesterSongs', params: { name } }),
    queryKey: () => musicQueueKey(toValue(channelId)),
    mutationFn: async ({ name }: RequesterRemoval): Promise<MusicQueue> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/music/queue/remove-by-requester',
        {
          params: { path: { version, channelId: id } },
          body: { requestedBy: name },
        },
      );

      if (error !== undefined)
        throw failed('remove requester songs', response.status, error);

      return readQueue(id);
    },
    apply: (current: MusicQueue | undefined, { name }: RequesterRemoval): MusicQueue =>
      withoutRequester(current, name),
  });
}

// Banning a queued song takes it out of the queue and adds it to the blocked list.
export function useBanQueueItem(channelId: MaybeRefOrGetter<string | null>): RowMutation {
  const queries = useQueryClient();

  return useOptimisticSave<MusicQueue, { position: number }>({
    describe: ({ position }) => ({ key: 'banQueueItem', params: { position: String(position) } }),
    queryKey: () => musicQueueKey(toValue(channelId)),
    mutationFn: async ({ position }: { position: number }): Promise<MusicQueue> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/music/queue/{position}/ban',
        {
          params: {
            path: {
              version,
              channelId: id,
              position,
            },
          },
        },
      );

      if (error !== undefined)
        throw failed('ban song', response.status, error);

      await queries.invalidateQueries({ queryKey: blockedTracksKey(id) });

      return readQueue(id);
    },
    apply: (current: MusicQueue | undefined, { position }: { position: number }): MusicQueue =>
      leavesQueue(current, position),
  });
}

export function useBlockTrack(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<BlockedTrack[], Error, BlockTrack, number> {
  return useOptimisticSave<BlockedTrack[], BlockTrack>({
    describe: ({ title }) => ({ key: 'blockTrack', params: { title } }),
    queryKey: () => blockedTracksKey(toValue(channelId)),
    mutationFn: async (track: BlockTrack): Promise<BlockedTrack[]> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/music/blocked-tracks',
        {
          params: { path: { version, channelId: id } },
          body: track,
        },
      );

      if (error !== undefined)
        throw failed('block song', response.status, error);

      return readBlocked(id);
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
  });
}

export function useUnblockTrack(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<BlockedTrack[], Error, { blockedTrackId: string }, number> {
  return useOptimisticSave<BlockedTrack[], { blockedTrackId: string }>({
    describe: ({ blockedTrackId }) => ({ key: 'unblockTrack', params: { id: blockedTrackId } }),
    queryKey: () => blockedTracksKey(toValue(channelId)),
    mutationFn: async ({ blockedTrackId }: { blockedTrackId: string }): Promise<BlockedTrack[]> => {
      const id = toValue(channelId) as string;
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/music/blocked-tracks/{blockedTrackId}',
        {
          params: {
            path: {
              version,
              channelId: id,
              blockedTrackId,
            },
          },
        },
      );

      if (error !== undefined)
        throw failed('unblock song', response.status, error);

      return readBlocked(id);
    },
    apply: (
      current: BlockedTrack[] | undefined,
      { blockedTrackId }: { blockedTrackId: string },
    ): BlockedTrack[] =>
      (current ?? []).filter((track) => track.id !== blockedTrackId),
  });
}

function definedOnly(change: MusicConfigChange): Partial<MusicConfig> {
  return Object.fromEntries(
    Object.entries(change).filter(([, value]) => value !== undefined && value !== null),
  ) as Partial<MusicConfig>;
}

// Only the changed fields travel in the PUT; the cache shows them at once and then holds what the bot returns.
export function useSaveMusicConfig(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<MusicConfig | null, Error, MusicConfigChange, number> {
  return useOptimisticSave<MusicConfig | null, MusicConfigChange>({
    describe: () => ({ key: 'saveMusicSettings' }),
    queryKey: () => musicConfigKey(toValue(channelId)),
    mutationFn: async (change: MusicConfigChange): Promise<MusicConfig | null> => {
      const id = toValue(channelId) as string;
      const { data, error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/music/config',
        {
          params: { path: { version, channelId: id } },
          body: change,
        },
      );

      if (error !== undefined)
        throw failed('save music config', response.status, error);

      return data?.data ?? null;
    },
    apply: (
      current: MusicConfig | null | undefined,
      change: MusicConfigChange,
    ): MusicConfig | null =>
      current
        ? { ...current, ...definedOnly(change) }
        : null,
  });
}

export type MusicPlaylist = components['schemas']['MusicPlaylist'];

const playlistLimit = 50;

export const musicPlaylistsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'music-playlists',
];

async function readPlaylists(channelId: string): Promise<MusicPlaylist[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/music/playlists',
    {
      params: { path: { version, channelId }, query: { limit: playlistLimit } },
    },
  );

  if (error !== undefined)
    throw failed('music playlists', response.status, error);

  return data?.data ?? [];
}

// The playlists of the connected music account; the banger picker lists them.
export function useMusicPlaylists(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<MusicPlaylist[], Error> {
  return channelQuery(channelId, musicPlaylistsKey, readPlaylists);
}

export type SongHistoryItem = components['schemas']['SongHistoryItem'];
export type TopRequester = components['schemas']['TopRequester'];

const historyLimit = 12;
const topRequesterLimit = 8;

// Both panels read the stored request rows, so the realtime layer marks them stale together.
export const musicHistoryKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'music-history',
];

export const musicTopRequestersKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'music-top-requesters',
];

async function readHistory(channelId: string): Promise<SongHistoryItem[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/music/history',
    {
      params: { path: { version, channelId }, query: { limit: historyLimit } },
    },
  );

  if (error !== undefined)
    throw failed('music history', response.status, error);

  return data?.data ?? [];
}

async function readTopRequesters(channelId: string): Promise<TopRequester[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/music/top-requesters',
    {
      params: { path: { version, channelId }, query: { limit: topRequesterLimit } },
    },
  );

  if (error !== undefined)
    throw failed('top requesters', response.status, error);

  return data?.data ?? [];
}

export function useMusicHistory(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<SongHistoryItem[], Error> {
  return channelQuery(channelId, musicHistoryKey, readHistory);
}

export function useMusicTopRequesters(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<TopRequester[], Error> {
  return channelQuery(channelId, musicTopRequestersKey, readTopRequesters);
}

export const musicHistoryOptions = (id: string) => channelOptions(musicHistoryKey, readHistory, id);

export const musicTopRequestersOptions = (id: string) =>
  channelOptions(musicTopRequestersKey, readTopRequesters, id);
