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

export type EventResponse = components['schemas']['EventResponseListItem'];

const version = '1';
// One response per event type; the event catalog stays well under this.
const pageSize = 200;

export const eventResponsesKey = (channelId: string | null) => ['channels', channelId, 'event-responses'];

export function useEventResponses(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<EventResponse[], Error> {
  return channelQuery(
    channelId,
    eventResponsesKey,
    async (id: string): Promise<EventResponse[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/channels/{channelId}/event-responses',
        {
          params: { path: { version, channelId: id }, query: { PageSize: pageSize } },
        },
      );

      if (error !== undefined)
        throw failed('event responses', response.status);

      return [...(data?.data ?? [])];
    },
  );
}
