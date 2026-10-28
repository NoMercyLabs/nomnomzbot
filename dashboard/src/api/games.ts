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
import type { components } from './schema';

// The chat games of a channel: the games it can start, the game that runs now, the finished ones, and the
// mini-game setups (the economy games).
export type GameSession = components['schemas']['GameSession'];
export type GameCatalogEntry = components['schemas']['LiveGameCatalogEntry'];
export type GameConfig = components['schemas']['GameConfig'];

const version = '1';
const historyPageSize = 25;

export const gamesKey = (channelId: string | null): KeyParts => ['channels', channelId, 'games'];

export const gameCatalogKey = (channelId: string | null): KeyParts => [
  ...gamesKey(channelId),
  'catalog',
];

export const activeGameKey = (channelId: string | null): KeyParts => [
  ...gamesKey(channelId),
  'active',
];

export const gameHistoryKey = (channelId: string | null): KeyParts => [
  ...gamesKey(channelId),
  'history',
];

export const gameConfigsKey = (channelId: string | null): KeyParts => [
  ...gamesKey(channelId),
  'configs',
];

async function readCatalog(channelId: string): Promise<GameCatalogEntry[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/games/sessions/catalog',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined)
    throw failed('live game catalog', response.status, error);

  return [...(data?.data ?? [])];
}

// No running game is a normal answer: the server may send an empty body or a 404.
async function readActive(channelId: string): Promise<GameSession | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/games/sessions/active',
    { params: { path: { version, channelId } } },
  );

  if (response.status === 404)
    return null;

  if (error !== undefined)
    throw failed('active live game', response.status, error);

  return data?.data ?? null;
}

async function readHistory(channelId: string): Promise<GameSession[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/games/sessions',
    { params: { path: { version, channelId }, query: { PageSize: historyPageSize } } },
  );

  if (error !== undefined)
    throw failed('live game history', response.status, error);

  return [...(data?.data ?? [])];
}

async function readConfigs(channelId: string): Promise<GameConfig[]> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/economy/games',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined)
    throw failed('game configs', response.status, error);

  return [...(data?.data ?? [])];
}

export function useGameCatalog(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<GameCatalogEntry[], Error> {
  return channelQuery(channelId, gameCatalogKey, readCatalog);
}

export function useActiveGame(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<GameSession | null, Error> {
  return channelQuery(channelId, activeGameKey, readActive);
}

export function useGameHistory(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<GameSession[], Error> {
  return channelQuery(channelId, gameHistoryKey, readHistory);
}

export function useGameConfigs(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<GameConfig[], Error> {
  return channelQuery(channelId, gameConfigsKey, readConfigs);
}

// Starts a game. The bot refuses when a game already runs or the game needs an entry fee setup; its reason
// stays on the mutation error.
export function useStartGame(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (gameType: string): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/channels/{channelId}/games/sessions',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { gameType },
        },
      );

      if (error !== undefined)
        throw refusal('start live game', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: gamesKey(toValue(channelId)) });
    },
  });
}

// Cancels a running game. The bot refunds every entry fee.
export function useCancelGame(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, string, undefined>({
    mutationFn: async (sessionId: string): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/channels/{channelId}/games/sessions/{sessionId}',
        {
          params: {
            path: {
              version,
              channelId: toValue(channelId) as string,
              sessionId,
            },
          },
        },
      );

      if (error !== undefined)
        throw refusal('cancel live game', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: gamesKey(toValue(channelId)) });
    },
  });
}

// Saves the whole setup of a mini-game (limits, odds, payout, plays per stream).
export function useSaveGameConfig(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, GameConfig, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, GameConfig, undefined>({
    mutationFn: async (config): Promise<void> => {
      const { id: _id, cooldownFloorSeconds: _floor, ...rest } = config;
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/economy/games',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: rest,
        },
      );

      if (error !== undefined)
        throw refusal('save game config', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: gameConfigsKey(toValue(channelId)) });
    },
  });
}

// Turns a mini-game on or off. The PUT takes the whole setup, so the rest of the row travels unchanged.
export function useSetGameEnabled(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, { config: GameConfig; enabled: boolean }, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, { config: GameConfig; enabled: boolean }, undefined>({
    mutationFn: async ({ config, enabled }): Promise<void> => {
      const { id: _id, cooldownFloorSeconds: _floor, ...rest } = config;
      const { error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/economy/games',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { ...rest, isEnabled: enabled },
        },
      );

      if (error !== undefined)
        throw refusal('save game config', error, response.status);
    },
    onSuccess: async (): Promise<void> => {
      await queries.invalidateQueries({ queryKey: gameConfigsKey(toValue(channelId)) });
    },
  });
}
