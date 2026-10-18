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
