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
