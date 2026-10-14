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

import { client } from './client';
import type { components } from './schema';

export type CurrencyConfig = components['schemas']['CurrencyConfigDto'];

export const currencyConfigKey = (channelId: string | null): unknown[] => [
  'channels',
  channelId,
  'economy',
  'config',
];

// The channel's own currency: its name and whether it is switched on. Null when the channel has none.
export function useCurrencyConfig(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<CurrencyConfig | null, Error> {
  return useQuery({
    queryKey: computed(() => currencyConfigKey(toValue(channelId))),
    queryFn: async (): Promise<CurrencyConfig | null> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/economy/config',
        { params: { path: { version: '1', channelId: toValue(channelId) as string } } },
      );

      if (error !== undefined)
        throw new Error(`currency config ${response.status}`);

      return data?.data ?? null;
    },
    enabled: computed(() => toValue(channelId) !== null),
  });
}
