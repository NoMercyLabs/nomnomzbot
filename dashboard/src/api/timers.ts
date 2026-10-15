// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { type UseQueryReturnType } from '@tanstack/vue-query';
import { type MaybeRefOrGetter } from 'vue';

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

export type Timer = components['schemas']['TimerListItem'];

const version = '1';
const pageSize = 100;

export const timersKey = (channelId: string | null) => ['channels', channelId, 'timers'];

export function useTimers(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<Timer[], Error> {
  return channelQuery(channelId, timersKey, async (id: string): Promise<Timer[]> => {
    const { data, error, response } = await client.GET(
      '/api/v{version}/channels/{channelId}/timers',
      {
        params: { path: { version, channelId: id }, query: { PageSize: pageSize } },
      },
    );

    if (error !== undefined)
      throw failed('timers', response.status);

    return [...(data?.data ?? [])];
  });
}
