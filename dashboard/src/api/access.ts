// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { computed, type ComputedRef, type MaybeRefOrGetter } from 'vue';

import { channelQuery, failed } from './channelQuery';
import { client } from './client';
import type { components } from './schema';

export type ResolvedAccess = components['schemas']['ResolvedAccessDto'];

export type ChannelAccess = {
  known: ComputedRef<boolean>;
  can: (actionKey: string) => boolean;
};

const version = '1';

export const channelAccessKey = (channelId: string | null): unknown[] => [
  'channels',
  channelId,
  'access',
];

async function readAccess(channelId: string): Promise<ResolvedAccess> {
  const { data, error, response } = await client.GET(
    '/api/v{version}/channels/{channelId}/roles/effective/me',
    { params: { path: { version, channelId } } },
  );

  if (error !== undefined || !data?.data)
    throw failed('channel access', response.status, error);

  return data.data;
}

// What the signed-in person may do in a channel: the action keys the bot's own gate checks
// ([RequireAction] on every endpoint), the broadcaster's overrides included. Until the bot has
// answered, nothing is allowed, so no control offers a change that cannot save.
export function useChannelAccess(channelId: MaybeRefOrGetter<string | null>): ChannelAccess {
  const query = channelQuery(channelId, channelAccessKey, readAccess);
  const keys = computed(() => new Set<string>(query.data.value?.heldActionKeys ?? []));

  return {
    known: computed(() => query.data.value !== undefined),
    can: (actionKey) => keys.value.has(actionKey),
  };
}
