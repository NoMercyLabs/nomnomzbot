// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The signed-in person's favorite emotes, kept on the server so they follow them to every device and channel.
import { useQuery, type UseMutationReturnType, type UseQueryReturnType } from '@tanstack/vue-query';

import { useOptimisticSave } from '@/composables/useOptimisticSave';
import { merge } from '@/lib/array';

import { client } from './client';
import type { components } from './schema';

export type FavoriteEmote = components['schemas']['FavoriteEmoteDto'];

export type FavoriteChange = {
  emote: Omit<FavoriteEmote, 'createdAt'>;
  favorite: boolean;
};

const version = { path: { version: '1' } } as const;

export const favoriteEmotesKey = ['users', 'me', 'favorite-emotes'] as const;

async function fetchFavorites(): Promise<FavoriteEmote[]> {
  const { data, error } = await client.GET('/api/v{version}/users/me/favorite-emotes', { params: version });

  if (error)
    throw new Error('favorite emotes failed');

  return [...(data?.data ?? [])];
}

export function useFavoriteEmotes(): UseQueryReturnType<FavoriteEmote[], Error> {
  return useQuery({
    queryKey: favoriteEmotesKey,
    queryFn: fetchFavorites,
    staleTime: 5 * 60_000,
  });
}

type EmoteRef = Pick<FavoriteEmote, 'provider' | 'emoteId'>;

function same(a: EmoteRef, b: EmoteRef): boolean {
  return a.provider === b.provider && a.emoteId === b.emoteId;
}

async function save({ emote, favorite }: FavoriteChange): Promise<FavoriteEmote[]> {
  const { error } = favorite
    ? await client.PUT('/api/v{version}/users/me/favorite-emotes', { params: version, body: emote })
    : await client.DELETE('/api/v{version}/users/me/favorite-emotes/{provider}/{emoteId}', {
        params: { path: {
          version: '1',
          provider: emote.provider,
          emoteId: emote.emoteId,
        } },
      });

  if (error)
    throw new Error('favorite emote change failed');

  return fetchFavorites();
}

// A star goes on or off at once; a failed save rolls it back and reaches the error inbox.
function applyChange(
  current: FavoriteEmote[] | undefined,
  { emote, favorite }: FavoriteChange,
): FavoriteEmote[] {
  const others = (current ?? []).filter((item) =>
    !same(item, emote));

  return favorite
    ? [...merge([{ ...emote, createdAt: new Date().toISOString() }], others)]
    : others;
}

type FavoriteSave = UseMutationReturnType<FavoriteEmote[], Error, FavoriteChange, number>;

// A star goes on or off at once; a failed save rolls it back and reaches the error inbox.
export function useFavoriteEmoteChange(): FavoriteSave {
  return useOptimisticSave<FavoriteEmote[], FavoriteChange>({
    queryKey: favoriteEmotesKey,
    mutationFn: save,
    apply: applyChange,
  });
}
