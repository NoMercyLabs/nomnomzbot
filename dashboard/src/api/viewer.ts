// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useQuery, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

export type ViewerChannel = components['schemas']['SongRequestPageDto'];
export type ViewerSummary = components['schemas']['ViewerSelfSummaryDto'];
export type ViewerQueue = components['schemas']['ViewerQueueDto'];
export type ViewerQueuedSong = components['schemas']['ViewerQueuedSongDto'];

const version = '1';

export const viewerChannelKey = (name: string): KeyParts => ['viewer', 'channel', name];
export const viewerSummaryKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'viewer',
  'summary',
];
export const viewerQueueKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'viewer',
  'queue',
];

// The channel a viewer link names, looked up by its Twitch name the way the public song-request page does.
export function useViewerChannel(
  name: MaybeRefOrGetter<string>,
): UseQueryReturnType<ViewerChannel, Error> {
  return useQuery({
    queryKey: computed(() => viewerChannelKey(toValue(name))),
    queryFn: async (): Promise<ViewerChannel> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/public/sr/by-channel/{channelName}',
        { params: { path: { version, channelName: toValue(name) } } },
      );

      if (error !== undefined || !data?.data)
        throw failed('viewer channel', response.status, error);

      return data.data;
    },
    retry: false,
  });
}

// The signed-in viewer's own tiles in one channel.
export function useViewerSummary(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ViewerSummary, Error> {
  return channelQuery(
    channelId,
    viewerSummaryKey,
    async (id: string): Promise<ViewerSummary> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/viewer/me',
        { params: { path: { version, channelId: id } } },
      );

      if (error !== undefined || !data?.data)
        throw failed('viewer summary', response.status, error);

      return data.data;
    },
  );
}

// Only the viewer's own songs in the channel's queue, with their place in line.
export function useViewerQueue(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ViewerQueue, Error> {
  return channelQuery(
    channelId,
    viewerQueueKey,
    async (id: string): Promise<ViewerQueue> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/viewer/me/queue',
        { params: { path: { version, channelId: id } } },
      );

      if (error !== undefined || !data?.data)
        throw failed('viewer queue', response.status, error);

      return data.data;
    },
  );
}
