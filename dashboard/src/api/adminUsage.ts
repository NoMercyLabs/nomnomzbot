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
