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
  useQuery,
  useQueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import type { BotDeviceLogin, BotLoginPoll } from './whiteLabelBot';
import type { KeyParts } from '@/lib/array';

export type PlatformBotStatus = components['schemas']['PlatformBotAdminStatus'];

// The reconnect confirmation: the operator's reason and the channel count the preview showed.
export type ReconnectConfirmation = {
  justification: string;
  confirmedAffectedChannelCount: number;
};

export type ReconnectPoll = ReconnectConfirmation & { deviceCode: string };

const version = '1';

export const platformBotPrefix: KeyParts = ['admin', 'platform-bot'];

export function useAdminPlatformBot(): UseQueryReturnType<PlatformBotStatus | null, Error> {
  return useQuery({
    queryKey: [...platformBotPrefix, 'status'],
    queryFn: async (): Promise<PlatformBotStatus | null> => {
      const { data, error, response } = await client.GET('/api/v{version}/admin/platform-bot', {
        params: { path: { version } },
      });

      if (error !== undefined)
        throw problemError('platform bot', response.status, error);

      return data?.data ?? null;
    },
  });
}

// The preview is the counted blast radius of a reconnect. It runs when the operator asks for it, with the reason.
export function usePreviewPlatformBotReconnect(): UseMutationReturnType<
  number,
  Error,
  string,
  undefined
> {
  return useMutation<number, Error, string, undefined>({
    mutationFn: async (justification: string): Promise<number> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/platform-bot/reconnect/preview',
        { params: { path: { version }, query: { justification } } },
      );

      if (error !== undefined)
        throw problemError('platform bot preview', response.status, error);

      return Number(data?.data?.affectedChannelCount ?? 0);
    },
  });
}

export function useStartPlatformBotReconnect(): UseMutationReturnType<
  BotDeviceLogin,
  Error,
  ReconnectConfirmation,
  undefined
> {
  return useMutation<BotDeviceLogin, Error, ReconnectConfirmation, undefined>({
    mutationFn: async (body: ReconnectConfirmation): Promise<BotDeviceLogin> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/admin/platform-bot/reconnect/device',
        { params: { path: { version } }, body },
      );
      const started = data?.data;

      if (error !== undefined || started === undefined || started === null)
        throw problemError('platform bot reconnect', response.status, error);

      return started;
    },
  });
}

// One poll of the reconnect login. An authorized poll replaced the shared bot, so the status reloads.
export function usePollPlatformBotReconnect(): UseMutationReturnType<
  BotLoginPoll,
  Error,
  ReconnectPoll,
  undefined
> {
  const queries = useQueryClient();

  return useMutation<BotLoginPoll, Error, ReconnectPoll, undefined>({
    mutationFn: async (body: ReconnectPoll): Promise<BotLoginPoll> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/admin/platform-bot/reconnect/device/poll',
        { params: { path: { version } }, body },
      );
      const poll = data?.data;

      if (error !== undefined || poll === undefined || poll === null)
        throw problemError('platform bot reconnect poll', response.status, error);

      return poll;
    },
    onSuccess: (poll: BotLoginPoll): void => {
      if (poll.status === 'authorized') {
        void queries.invalidateQueries({ queryKey: platformBotPrefix });
      }
    },
  });
}
