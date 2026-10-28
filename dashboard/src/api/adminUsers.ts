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

import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type AdminUser = components['schemas']['AdminUser'];
export type AdminUsersPage = components['schemas']['PaginatedResponseOfAdminUser'];

export type AdminUsersParams = {
  search?: string;
  role?: string;
  page: number;
  take: number;
};

const version = '1';

export const adminUsersPrefix: KeyParts = ['admin', 'users'];

export const adminUsersKey = ({
  search,
  role,
  page,
  take,
}: AdminUsersParams): KeyParts => [
  ...adminUsersPrefix,
  {
    search,
    role,
    page,
    take,
  },
];

// An empty filter is left out of the request, not sent as an empty string.
function listQuery({
  search,
  role,
  page,
  take,
}: AdminUsersParams): Record<string, string | number> {
  const query: Record<string, string | number> = { Page: page, Take: take };

  if (search) {
    query.search = search;
  }

  if (role) {
    query.role = role;
  }

  return query;
}

export function useAdminUsers(
  params: MaybeRefOrGetter<AdminUsersParams>,
): UseQueryReturnType<AdminUsersPage, Error> {
  return useQuery({
    queryKey: computed(() => adminUsersKey(toValue(params))),
    queryFn: async (): Promise<AdminUsersPage> => {
      const { data, error, response } = await client.GET('/api/v{version}/admin/users', {
        params: { path: { version }, query: listQuery(toValue(params)) },
      });

      // A refusal is an error state, not an empty list.
      if (error !== undefined)
        throw problemError('admin users', response.status, error);

      return data ?? {};
    },
    placeholderData: keepPreviousData,
  });
}
