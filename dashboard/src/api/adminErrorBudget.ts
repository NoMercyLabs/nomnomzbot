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

export type AdminTenantErrorBudget = components['schemas']['AdminTenantErrorBudget'];

export type AdminTenantErrorBudgetPage =
  components['schemas']['PaginatedResponseOfAdminTenantErrorBudget'];

const version = '1';

export const adminErrorBudgetPrefix: KeyParts = ['admin', 'error-budget'];

function errorBudgetKey(params: AdminPageParams): KeyParts {
  const joined = merge(adminErrorBudgetPrefix, [{ ...params }]);

  return Array.from(joined);
}

export function useAdminErrorBudget(
  params: MaybeRefOrGetter<AdminPageParams>,
): UseQueryReturnType<AdminTenantErrorBudgetPage, Error> {
  return useQuery({
    queryKey: computed((): KeyParts => errorBudgetKey(toValue(params))),
    queryFn: async (): Promise<AdminTenantErrorBudgetPage> => {
      const { page, take } = toValue(params);
      const { data, error, response } = await client.GET('/api/v{version}/admin/error-budget', {
        params: { path: { version }, query: { Page: page, Take: take } },
      });

      if (error !== undefined)
        throw problemError('admin error budget', response.status, error);

      return data ?? {};
    },
    placeholderData: keepPreviousData,
  });
}
