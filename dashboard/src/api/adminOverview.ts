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

import { useQuery, type UseQueryReturnType } from '@tanstack/vue-query';

import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type AdminStats = components['schemas']['AdminStats'];

const version = '1';

// The platform numbers change all day, so the page asks again on its own.
const STATS_REFRESH_MS: number = 30_000;

export const adminStatsKey: KeyParts = ['admin', 'stats'];

export function useAdminStats(): UseQueryReturnType<AdminStats | null, Error> {
  return useQuery({
    queryKey: adminStatsKey,
    queryFn: async (): Promise<AdminStats | null> => {
      const { data, error, response } = await client.GET('/api/v{version}/admin/stats', {
        params: { path: { version } },
      });

      if (error !== undefined)
        throw problemError('admin stats', response.status, error);

      return data?.data ?? null;
    },
    refetchInterval: STATS_REFRESH_MS,
  });
}
