// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { UseQueryReturnType } from '@tanstack/vue-query';
import type { MaybeRefOrGetter } from 'vue';

import type { KeyParts } from '@/lib/array';

import { channelOptions, channelQuery } from './channelQuery';
import { client } from './client';
import { failed } from './errors';
import type { components } from './schema';

export type SupporterEvent = components['schemas']['SupporterEventDto'];

const version = '1';
const pageSize = 50;

export const supporterEventsKey = (channelId: string | null): KeyParts => [
  'supporters',
  channelId,
  'events',
];

// Tips, memberships, merch and charity from the connected providers, newest first.
export function useSupporterEvents(
  channelId: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<SupporterEvent[], Error> {
  return channelQuery(channelId, supporterEventsKey, readSupporterEvents);
}

export const supporterEventsOptions = (id: string) =>
  channelOptions(supporterEventsKey, readSupporterEvents, id);

async function readSupporterEvents(): Promise<SupporterEvent[]> {
  const { data, error, response } = await client.GET('/api/v{version}/supporters/events', {
    params: {
      path: { version },
      query: { PageSize: pageSize },
    },
  });

  if (error !== undefined)
    throw failed('supporter events', response.status, error);

  return data?.data ?? [];
}
