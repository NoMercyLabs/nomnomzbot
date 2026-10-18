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

import type { AdminPageParams } from './adminActivity';
import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import { merge, type KeyParts } from '@/lib/array';

export type AdminTenantUsage = components['schemas']['AdminTenantUsage'];
export type AdminTenantUsageMetric = components['schemas']['AdminTenantUsageMetric'];
export type AdminTenantUsagePage = components['schemas']['PaginatedResponseOfAdminTenantUsage'];

const version = '1';

export const adminTenantUsagePrefix: KeyParts = ['admin', 'tenant-usage'];

function tenantUsageKey(params: AdminPageParams): KeyParts {
  const joined = merge(adminTenantUsagePrefix, [{ ...params }]);

  return Array.from(joined);
}

export function useAdminTenantUsage(
  params: MaybeRefOrGetter<AdminPageParams>,
): UseQueryReturnType<AdminTenantUsagePage, Error> {
  return useQuery({
    queryKey: computed((): KeyParts => tenantUsageKey(toValue(params))),
    queryFn: async (): Promise<AdminTenantUsagePage> => {
      const { page, take } = toValue(params);
      const { data, error, response } = await client.GET('/api/v{version}/admin/usage', {
        params: { path: { version }, query: { Page: page, Take: take } },
      });

      if (error !== undefined)
        throw problemError('admin usage', response.status, error);

      return data ?? {};
    },
    placeholderData: keepPreviousData,
  });
}
