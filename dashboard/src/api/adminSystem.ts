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

import { useMutation, useQuery, type UseQueryReturnType } from '@tanstack/vue-query';

import { adminSystemStatusKey } from './admin';
import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import type { AdminSystemStatusPayload } from '@/realtime/payloads';

export type ServiceHealth = components['schemas']['ServiceHealth'];
export type KeyRotationSummary = components['schemas']['DekRotationSummary'];

// The health needs the access key and the counters need the analytics key. A person who holds only the
// access key still sees the health; the counters are null for them instead of failing the whole page.
export type AdminSystemSnapshot = AdminSystemStatusPayload;

// The two root keys of one rotation: the key the stored data keys are sealed under now, and the new one.
export type KeyRotation = {
  previousKey: string;
  currentKey: string;
};

const version = '1';
const FORBIDDEN = 403;

// The bot also pushes this snapshot over the admin hub every 15 seconds; the poll covers a dropped hub.
const SYSTEM_REFRESH_MS: number = 60_000;

// One snapshot in the shape the hub pushes, so a push replaces the cached answer of this query.
export function useAdminSystemStatus(): UseQueryReturnType<AdminSystemSnapshot, Error> {
  return useQuery({
    queryKey: adminSystemStatusKey,
    queryFn: async (): Promise<AdminSystemSnapshot> => {
      const [health, totals] = await Promise.all([
        client.GET('/api/v{version}/admin/system', { params: { path: { version } } }),
        client.GET('/api/v{version}/admin/stats', { params: { path: { version } } }),
      ]);

      if (health.error !== undefined)
        throw problemError('admin system', health.response.status, health.error);

      if (totals.error !== undefined && totals.response.status !== FORBIDDEN)
        throw problemError('admin stats', totals.response.status, totals.error);

      if (health.data?.data == null)
        throw problemError('admin system', health.response.status, undefined);

      return { system: health.data.data, stats: totals.data?.data ?? null };
    },
    refetchInterval: SYSTEM_REFRESH_MS,
  });
}

// Re-wraps every stored data key from the previous root key to the current one. Idempotent on the server.
export function useRotateEncryptionKey() {
  return useMutation({
    mutationFn: async (rotation: KeyRotation): Promise<KeyRotationSummary> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/admin/security/rotate-encryption-key',
        { params: { path: { version } }, body: rotation },
      );

      if (error !== undefined || data?.data == null)
        throw problemError('admin key rotation', response.status, error);

      return data.data;
    },
  });
}
