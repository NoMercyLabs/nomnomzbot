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

export type ViewerChannel = components['schemas']['SongRequestPage'];
export type ViewerSummary = components['schemas']['ViewerSelfSummary'];
export type ViewerQueue = components['schemas']['ViewerQueue'];
export type ViewerQueuedSong = components['schemas']['ViewerQueuedSong'];

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

export type WatchedChannel = components['schemas']['WatchedChannel'];

export const watchedChannelsKey: KeyParts = ['viewer', 'watched-channels'];

// The channels where the signed-in person has viewer activity, newest first. Never their own channel.
export function useWatchedChannels(): UseQueryReturnType<WatchedChannel[], Error> {
  return useQuery({
    queryKey: watchedChannelsKey,
    queryFn: async (): Promise<WatchedChannel[]> => {
      const { data, error, response } = await client.GET('/api/v{version}/me/watched-channels', {
        params: { path: { version } },
      });

      if (error !== undefined)
        throw failed('watched channels', response.status, error);

      return data?.data ?? [];
    },
    retry: false,
  });
}

export type PublicProfile = components['schemas']['PublicProfile'];

export const publicProfileKey = (channelId: string | null, userId: string | null): KeyParts => [
  'channels',
  channelId,
  'viewer',
  'profile',
  userId,
];

// What any signed-in person may see of another person in a channel: no notes, no moderation, no bot standing.
export function usePublicProfile(
  channelId: MaybeRefOrGetter<string | null>,
  userId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<PublicProfile, Error> {
  return useQuery({
    queryKey: computed(() => publicProfileKey(toValue(channelId), toValue(userId))),
    queryFn: async (): Promise<PublicProfile> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/chat/viewer/users/{userId}/profile',
        { params: { path: {
          version,
          channelId: toValue(channelId)!,
          userId: toValue(userId)!,
        } } },
      );

      if (error !== undefined || !data?.data)
        throw failed('public profile', response.status, error);

      return data.data;
    },
    enabled: computed(() => toValue(channelId) !== null && toValue(userId) !== null),
  });
}
