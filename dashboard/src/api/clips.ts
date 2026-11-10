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

// The channel's Twitch clips, and the upload of one clip to the streamer's YouTube channel.
import {
  useInfiniteQuery,
  useMutation,
  useQueryClient,
  type InfiniteData,
  type UseInfiniteQueryReturnType,
  type UseMutationReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import { failed } from './errors';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type ChannelClips = components['schemas']['ChannelClips'];
export type ChannelClip = components['schemas']['ChannelClip'];
export type ClipUpload = components['schemas']['YouTubeClipUpload'];
export type ClipUploadWords = components['schemas']['YouTubeClipUploadRequest'];

// The privacy a YouTube video can have; the first is what a new upload gets.
export const VIDEO_PRIVACY = ['private', 'unlisted', 'public'] as const;

export type VideoPrivacy = (typeof VIDEO_PRIVACY)[number];

type Cursor = string | null;

const version = '1';

export const clipsKey = (channelId: string | null): KeyParts => ['clips', channelId];

async function readPage(channelId: string, cursor: Cursor): Promise<ChannelClips | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/clips',
    {
      params: {
        path: { version, channelId },
        query: cursor === null
          ? {}
          : { cursor },
      },
    },
  );

  if (error !== undefined)
    throw failed('clips', response.status, error);

  return data?.data ?? null;
}

// Pages of the channel's clips, newest first. Each page says how many YouTube uploads the app has left today.
export function useClips(
  channelId: MaybeRefOrGetter<string | null>,
): UseInfiniteQueryReturnType<InfiniteData<ChannelClips | null, Cursor>, Error> {
  return useInfiniteQuery({
    queryKey: computed(() => clipsKey(toValue(channelId))),
    queryFn: ({ pageParam }: { pageParam: Cursor }): Promise<ChannelClips | null> =>
      readPage(toValue(channelId) as string, pageParam),
    initialPageParam: null as Cursor,
    getNextPageParam: (last: ChannelClips | null): Cursor | undefined =>
      last?.nextCursor ?? undefined,
    enabled: computed(() => toValue(channelId) !== null),
  });
}

// Puts one clip on the streamer's YouTube channel. The answer's `outcome` says what happened.
export function useUploadClip(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<
  ClipUpload | null,
  Error,
  { clipId: string; words: ClipUploadWords },
  undefined
> {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({
      clipId,
      words,
    }: {
      clipId: string;
      words: ClipUploadWords;
    }): Promise<ClipUpload | null> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/clips/{clipId}/youtube',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              clipId,
            },
          },
          body: words,
        },
      );

      if (error !== undefined)
        throw failed('clip upload', response.status, error);

      return data?.data ?? null;
    },
    // The uploads left today changed, so the list is read again.
    onSuccess: () => queries.invalidateQueries({ queryKey: clipsKey(toValue(channelId)) }),
  });
}
