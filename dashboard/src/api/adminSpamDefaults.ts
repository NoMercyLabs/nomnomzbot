// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import {
  useMutation,
  useQuery,
  useQueryClient,
  type UseQueryReturnType,
} from '@tanstack/vue-query';

import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type SpamPolicy = components['schemas']['SpamDefensePolicy'];
export type SpamSettings = components['schemas']['SpamDefenseSettings'];
export type SpamSetting = components['schemas']['SpamSettingDescriptor'];
export type SpamInvariant = components['schemas']['SpamInvariant'];
export type SpamTrustThresholds = components['schemas']['TrustTierThresholds'];

const version = '1';

export const adminSpamDefaultsKey: KeyParts = ['admin', 'spam-defaults'];

// The platform defaults every new channel starts with, plus the catalog that bounds and groups each knob.
export function useAdminSpamDefaults(): UseQueryReturnType<SpamPolicy, Error> {
  return useQuery({
    queryKey: adminSpamDefaultsKey,
    queryFn: async (): Promise<SpamPolicy> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/spam-defense/defaults',
        { params: { path: { version } } },
      );

      if (error !== undefined)
        throw problemError('admin spam defaults', response.status, error);

      if (!data?.data)
        throw problemError('admin spam defaults', response.status, { message: 'empty' });

      return data.data;
    },
  });
}

// A saved set of defaults changes what the next read returns, so the cached policy is refetched.
export function useSaveAdminSpamDefaults() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (body: SpamSettings): Promise<void> => {
      const { error, response } = await client.PUT('/api/v{version}/admin/spam-defense/defaults', {
        params: { path: { version } },
        body,
      });

      if (error !== undefined)
        throw problemError('admin spam defaults', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: adminSpamDefaultsKey }),
  });
}
