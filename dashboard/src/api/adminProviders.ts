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

export type ProviderCredential = components['schemas']['ProviderCredential'];
export type SaveProviderCredentialRequest = components['schemas']['SaveProviderCredentialRequest'];

export type SaveProviderTarget = {
  provider: string;
  body: SaveProviderCredentialRequest;
};

const version = '1';

export const adminProvidersKey: KeyParts = ['admin', 'providers'];

// Every provider's OAuth app state. The bot never returns a secret, only which source wins.
export function useAdminProviders(): UseQueryReturnType<ProviderCredential[], Error> {
  return useQuery({
    queryKey: adminProvidersKey,
    queryFn: async (): Promise<ProviderCredential[]> => {
      const { data, error, response } = await client.GET('/api/v{version}/admin/providers', {
        params: { path: { version } },
      });

      if (error !== undefined)
        throw problemError('admin providers', response.status, error);

      return data?.data ?? [];
    },
  });
}

// A blank field is left untouched by the bot, so the page sends only what the admin typed.
export function useSaveProviderCredential() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ provider, body }: SaveProviderTarget): Promise<void> => {
      const { error, response } = await client.PUT('/api/v{version}/admin/providers/{provider}', {
        params: { path: { version, provider } },
        body,
      });

      if (error !== undefined)
        throw problemError('admin provider save', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: adminProvidersKey }),
  });
}

// Clears the stored credentials; resolution falls back to the environment.
export function useClearProviderCredential() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (provider: string): Promise<void> => {
      const { error, response } = await client.DELETE('/api/v{version}/admin/providers/{provider}', {
        params: { path: { version, provider } },
      });

      if (error !== undefined)
        throw problemError('admin provider clear', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: adminProvidersKey }),
  });
}
