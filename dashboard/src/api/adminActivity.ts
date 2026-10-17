// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { keepPreviousData, useQuery, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import { merge, type KeyParts } from '@/lib/array';

export type AdminEventSubTenantHealth = components['schemas']['AdminEventSubTenantHealth'];
export type AdminEventSubTopicHealth = components['schemas']['AdminEventSubTopicHealth'];
export type AdminEventSubHealthPage =
  components['schemas']['PaginatedResponseOfAdminEventSubTenantHealth'];

export type AdminPageParams = {
  page: number;
  take: number;
};

const version = '1';

export const adminEventSubHealthPrefix: KeyParts = ['admin', 'eventsub-health'];

function eventSubHealthKey(params: AdminPageParams): KeyParts {
  const joined = merge(adminEventSubHealthPrefix, [{ ...params }]);

  return Array.from(joined);
}

export function useAdminEventSubHealth(
  params: MaybeRefOrGetter<AdminPageParams>,
): UseQueryReturnType<AdminEventSubHealthPage, Error> {
  return useQuery({
    queryKey: computed((): KeyParts => eventSubHealthKey(toValue(params))),
    queryFn: async (): Promise<AdminEventSubHealthPage> => {
      const { page, take } = toValue(params);
      const { data, error, response } = await client.GET('/api/v{version}/admin/eventsub/health', {
        params: { path: { version }, query: { Page: page, Take: take } },
      });

      // A refusal is an error state, not an empty registry.
      if (error !== undefined)
        throw problemError('admin eventsub health', response.status, error);

      return data ?? {};
    },
    placeholderData: keepPreviousData,
  });
}
