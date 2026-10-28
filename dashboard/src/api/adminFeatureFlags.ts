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

export type FeatureFlag = components['schemas']['FeatureFlag'];
export type FeatureFlagOverride = components['schemas']['FeatureFlagOverride'];
export type FeatureFlagBlastRadius = components['schemas']['FeatureFlagBlastRadius'];
export type SetFeatureFlagRequest = components['schemas']['SetFeatureFlagRequest'];
export type SetFeatureFlagOverrideRequest = components['schemas']['SetFeatureFlagOverrideRequest'];

export type FlagOverrideTarget = {
  flagKey: string;
  broadcasterId: string;
};

const version = '1';

export const adminFeatureFlagsKey: KeyParts = ['admin', 'feature-flags'];
export const adminFeatureFlagOverridesKey: KeyParts = ['admin', 'feature-flags', 'overrides'];

// Every flag definition the platform knows.
export function useAdminFeatureFlags(): UseQueryReturnType<FeatureFlag[], Error> {
  return useQuery({
    queryKey: adminFeatureFlagsKey,
    queryFn: async (): Promise<FeatureFlag[]> => {
      const { data, error, response } = await client.GET('/api/v{version}/admin/feature-flags', {
        params: { path: { version } },
      });

      if (error !== undefined)
        throw problemError('admin feature flags', response.status, error);

      return data?.data ?? [];
    },
  });
}

// Every per-channel override on every flag: what is forced where.
export function useAdminFeatureFlagOverrides(): UseQueryReturnType<FeatureFlagOverride[], Error> {
  return useQuery({
    queryKey: adminFeatureFlagOverridesKey,
    queryFn: async (): Promise<FeatureFlagOverride[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/feature-flags/overrides',
        { params: { path: { version } } },
      );

      if (error !== undefined)
        throw problemError('admin flag overrides', response.status, error);

      return data?.data ?? [];
    },
  });
}

// The counted number of channels a flag change reaches, read before the change is saved.
export function useFlagBlastRadius() {
  return useMutation({
    mutationFn: async (flagKey: string): Promise<FeatureFlagBlastRadius> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/feature-flags/{flagKey}/blast-radius',
        { params: { path: { version, flagKey } } },
      );

      if (error !== undefined)
        throw problemError('admin flag blast radius', response.status, error);

      return data?.data ?? { tenantsAffected: 0, sampleChannelNames: [] };
    },
  });
}

// A saved definition changes the list and what each override sits on top of, so the flag list is refetched.
export function useSaveAdminFeatureFlag() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (body: SetFeatureFlagRequest): Promise<void> => {
      const { error, response } = await client.PUT('/api/v{version}/admin/feature-flags', {
        params: { path: { version } },
        body,
      });

      if (error !== undefined)
        throw problemError('admin feature flags', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: adminFeatureFlagsKey }),
  });
}

export function useSetFlagOverride() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({
      flagKey,
      broadcasterId,
      body,
    }: FlagOverrideTarget & { body: SetFeatureFlagOverrideRequest }): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/admin/feature-flags/{flagKey}/overrides/{broadcasterId}',
        { params: { path: {
          version,
          flagKey,
          broadcasterId,
        } }, body },
      );

      if (error !== undefined)
        throw problemError('admin flag overrides', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: adminFeatureFlagOverridesKey }),
  });
}

export function useDeleteFlagOverride() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ flagKey, broadcasterId }: FlagOverrideTarget): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/admin/feature-flags/{flagKey}/overrides/{broadcasterId}',
        { params: { path: {
          version,
          flagKey,
          broadcasterId,
        } } },
      );

      if (error !== undefined)
        throw problemError('admin flag overrides', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: adminFeatureFlagOverridesKey }),
  });
}
