// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useMutation, useQueryClient, type UseMutationReturnType } from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { failed } from './channelQuery';
import { client } from './client';
import { botStatusKey, type BotStatus } from './home';
import type { components } from './schema';

// The channel's own bot account (white-label bot): the account that speaks for the channel instead of the
// shared platform bot. The status itself is read by useBotStatus in home.ts; this file only changes it.
export type BotDeviceLogin = components['schemas']['DeviceCodeStart'];
export type BotLoginPoll = components['schemas']['DeviceBotPoll'];
export type BotBrowserLogin = components['schemas']['OAuthStart'];

const version = '1';

// The login is over when the poll says anything other than one of the two waiting answers.
export function isBotLoginWaiting(status: string | undefined): boolean {
  return status === 'pending' || status === 'slow_down';
}

export function useStartBotLogin(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<BotDeviceLogin, Error, void, undefined> {
  return useMutation<BotDeviceLogin, Error, void, undefined>({
    mutationFn: async (): Promise<BotDeviceLogin> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/auth/twitch/channels/{channelId}/bot/device',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      const started = data?.data;

      if (error !== undefined || started === undefined || started === null)
        throw failed('start bot login', response.status, error);

      return started;
    },
  });
}

// One poll of the device login. An authorized poll hands back the connected bot, which replaces the cached status.
export function usePollBotLogin(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<BotLoginPoll, Error, string, undefined> {
  const queries = useQueryClient();

  return useMutation<BotLoginPoll, Error, string, undefined>({
    mutationFn: async (deviceCode: string): Promise<BotLoginPoll> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/auth/twitch/channels/{channelId}/bot/device/poll',
        {
          params: { path: { version, channelId: toValue(channelId) as string } },
          body: { deviceCode },
        },
      );

      const poll = data?.data;

      if (error !== undefined || poll === undefined || poll === null)
        throw failed('poll bot login', response.status, error);

      return poll;
    },
    onSuccess: (poll: BotLoginPoll): void => {
      if (poll.status === 'authorized' && poll.bot) {
        const key = botStatusKey(toValue(channelId));

        queries.setQueryData<BotStatus | null>(key, poll.bot);
      }
    },
  });
}

// The other way in: the Twitch authorize page in a browser tab, for a person who can not use the code.
export function useStartBotBrowserLogin(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<BotBrowserLogin, Error, void, undefined> {
  return useMutation<BotBrowserLogin, Error, void, undefined>({
    mutationFn: async (): Promise<BotBrowserLogin> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/bot/connect',
        { params: { path: { version, channelId: toValue(channelId) as string } } },
      );

      const started = data?.data;

      if (error !== undefined || started === undefined || started === null)
        throw failed('start bot browser login', response.status, error);

      return started;
    },
  });
}

// The channel goes back to the shared bot. The status is dropped to "not connected" at once.
export function useDisconnectBot(
  channelId: MaybeRefOrGetter<string | null>,
): UseMutationReturnType<void, Error, void, undefined> {
  const queries = useQueryClient();

  return useMutation<void, Error, void, undefined>({
    mutationFn: async (): Promise<void> => {
      const { error, response } = await client.DELETE('/api/v{version}/channels/{channelId}/bot', {
        params: { path: { version, channelId: toValue(channelId) as string } },
      });

      if (error !== undefined)
        throw failed('disconnect bot', response.status, error);
    },
    onSuccess: (): void => {
      const key = botStatusKey(toValue(channelId));

      queries.setQueryData<BotStatus | null>(key, { connected: false });
    },
  });
}
