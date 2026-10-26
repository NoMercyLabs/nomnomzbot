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

import type { KeyParts } from '@/lib/array';

import { channelQuery, failed, refusal } from './channelQuery';
import { client } from './client';
import { connectionsKey } from './home';
import type { components } from './schema';

// One external service of the channel: what it is, whether it is connected and as whom.
export type ChannelIntegration = components['schemas']['Integration'];
export type BlastRadius = components['schemas']['BlastRadius'];
export type SpotifyCredentials = components['schemas']['ChannelSpotifyCredentials'];
export type NewSpotifyCredentials = components['schemas']['SetChannelSpotifyCredentials'];

const version = '1';

// The scope set each provider asks for when the streamer connects (server OAuthProviderRegistry).
// Spotify asks for the playlists too: !banger and the playlist pickers write to them.
export const connectScopeSets = {
  spotify: 'spotify.playback+spotify.library',
  kick: 'kick.chat',
  youtube: 'youtube.manage',
  patreon: 'patreon.supporters',
  treatstream: 'treatstream.treats',
  shopify: 'shopify.orders',
} as const satisfies Record<string, string>;

export const integrationsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'integrations',
];

export const spotifyCredentialsKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'integrations',
  'spotify-credentials',
];

async function readIntegrations(channelId: string): Promise<ChannelIntegration[]> {
  const { data, error, response } = await client.GET('/api/v{version}/channels/{channelId}/integrations', {
    params: { path: { channelId, version } },
  });

  const rows = data?.data?.integrations;

  if (error !== undefined || rows === undefined)
    throw failed('integrations', response.status, error);

  return rows;
}

async function readSpotifyCredentials(channelId: string): Promise<SpotifyCredentials> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/integrations/spotify/credentials',
    { params: { path: { channelId, version } } },
  );

  const row = data?.data;

  if (error !== undefined || row === undefined || row === null)
    throw failed('spotify credentials', response.status, error);

  return row;
}

export function useIntegrations(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ChannelIntegration[], Error> {
  return channelQuery(channelId, integrationsKey, readIntegrations);
}

export function useSpotifyCredentials(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<SpotifyCredentials, Error> {
  return channelQuery(channelId, spotifyCredentialsKey, readSpotifyCredentials);
}

// What stops working when the provider is disconnected: the real counts, read right before the confirm.
export function useDisconnectBlastRadius(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<BlastRadius, Error, string, undefined> {
  return useMutation<BlastRadius, Error, string, undefined>({
    mutationFn: async (integrationId: string): Promise<BlastRadius> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/integrations/{integrationId}/blast-radius',
        { params: { path: {
          channelId: toValue(channelId) as string,
          integrationId,
          version,
        } } },
      );

      const row = data?.data;

      if (error !== undefined || row === undefined || row === null)
        throw failed('disconnect impact', response.status, error);

      return row;
    },
  });
}

export function useDisconnectIntegration(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (integrationId: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/integrations/{integrationId}',
        { params: { path: {
          channelId: toValue(channelId) as string,
          integrationId,
          version,
        } } },
      );

      if (error !== undefined)
        throw refusal('disconnect integration', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: integrationsKey(toValue(channelId)) });
      await queries.invalidateQueries({ queryKey: connectionsKey(toValue(channelId)) });
    },
  });
}

// Starts a connect: the bot answers with the page of the service where the streamer says yes.
export function useConnectProvider(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<string, Error, { provider: string; shopDomain?: string }, undefined> {
  return useMutation<string, Error, { provider: string; shopDomain?: string }, undefined>({
    mutationFn: async (request: { provider: string; shopDomain?: string }): Promise<string> => {
      const scopeSetKey = (connectScopeSets as Record<string, string>)[request.provider] as string;

      const { data, error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/integrations/{provider}/connect',
        {
          params: { path: {
            channelId: toValue(channelId) as string,
            provider: request.provider,
            version,
          } },
          body: {
            scopeSetKey,
            returnUrl: window.location.href,
            shopDomain: request.shopDomain ?? null,
          },
        },
      );

      if (error !== undefined)
        throw refusal('connect integration', error, response.status);

      const url = data?.data?.authorizeUrl;

      if (!url)
        throw failed('connect integration', response.status);

      return url;
    },
  });
}

export function useSaveSpotifyCredentials(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<SpotifyCredentials, Error, NewSpotifyCredentials, undefined> {
  const queries = useQueryClient();

  return useMutation<SpotifyCredentials, Error, NewSpotifyCredentials, undefined>({
    mutationFn: async (credentials: NewSpotifyCredentials): Promise<SpotifyCredentials> => {
      const { data, error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/integrations/spotify/credentials',
        {
          params: { path: { channelId: toValue(channelId) as string, version } },
          body: credentials,
        },
      );

      const row = data?.data;

      if (error !== undefined)
        throw refusal('save spotify credentials', error, response.status);

      if (row === undefined || row === null)
        throw failed('save spotify credentials', response.status);

      return row;
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: spotifyCredentialsKey(toValue(channelId)) });
    },
  });
}

export function useClearSpotifyCredentials(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, void, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, void, undefined>({
    mutationFn: async (): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/integrations/spotify/credentials',
        { params: { path: { channelId: toValue(channelId) as string, version } } },
      );

      if (error !== undefined)
        throw refusal('clear spotify credentials', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: spotifyCredentialsKey(toValue(channelId)) });
    },
  });
}
