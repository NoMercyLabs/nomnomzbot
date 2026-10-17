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
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { toValue, type MaybeRefOrGetter } from 'vue';

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { ApiError } from './errors';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type ChatIdentity = components['schemas']['ChatIdentity'];
export type ScopeRegrant = components['schemas']['ScopeRegrantStart'];

const version = '1';

export const chatIdentityKey = (channelId: string | null): KeyParts => [
  'channels',
  channelId,
  'chat-identity',
];

async function readIdentity(channelId: string): Promise<ChatIdentity | null> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/chat-identity',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined)
    throw failed('chat identity', response.status, error);

  return data?.data ?? null;
}

// The owner's name color, the named colors Twitch offers everyone, and the badges on their latest line.
export function useChatIdentity(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<ChatIdentity | null, Error> {
  return channelQuery(channelId, chatIdentityKey, readIdentity);
}

// A named color or a hex value. Twitch refuses hex for users without Turbo or Prime; the error's
// detail carries the plain reason the panel shows.
export function useSetChatColor(channelId: MaybeRefOrGetter<string | null>) {
  const queries = useQueryClient();

  return useMutation<ChatIdentity | null, ApiError, string>({
    mutationFn: async (color: string): Promise<ChatIdentity | null> => {
      const id = toValue(channelId) as string;
      const { data, error, response } = await client.PUT(
        '/api/v{version}/channels/{channelId}/chat-identity/color',
        {
          params: { path: { version, channelId: id } },
          body: { color },
        },
      );

      if (!response.ok)
        throw failed('chat color', response.status, error);

      return data?.data ?? null;
    },
    onSuccess: (identity) => {
      const key = chatIdentityKey(toValue(channelId));

      queries.setQueryData(key, identity);
    },
  });
}

// Starts the additive re-consent: Twitch asks for the scopes the bot lacks on top of the ones it has,
// so nothing is signed out. The caller shows the code and polls the device approval.
export function useStartScopeRegrant() {
  return useMutation<ScopeRegrant | null, ApiError>({
    mutationFn: async (): Promise<ScopeRegrant | null> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/twitch/diagnostics/regrant',
        { params: { path: { version } } },
      );

      if (!response.ok)
        throw failed('scope regrant', response.status, error);

      return data?.data ?? null;
    },
  });
}
