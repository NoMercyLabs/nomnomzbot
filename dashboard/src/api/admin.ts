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
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
  type UseQueryReturnType,
} from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type AdminTenant = components['schemas']['AdminTenant'];
export type AdminTenantDetail = components['schemas']['AdminTenantDetail'];
export type AdminTenantsPage = components['schemas']['PaginatedResponseOfAdminTenant'];
export type AdminAccessGrant = components['schemas']['TenantAccessGrant'];
export type AdminImpersonationToken = components['schemas']['ImpersonationToken'];

export type AdminTenantKind = 'streamer' | 'viewer';

export type AdminTenantsParams = {
  search?: string;
  kind?: AdminTenantKind;
  status?: string;
  isLive?: boolean;
  page: number;
  take: number;
};

const version = '1';

export const adminSystemStatusKey: KeyParts = ['admin', 'system-status'];

// Every cached list page starts with this prefix, whatever its filters.
export const adminTenantsPrefix: KeyParts = ['admin', 'tenants'];

export const adminTenantsKey = ({
  search,
  kind,
  status,
  isLive,
  page,
  take,
}: AdminTenantsParams): KeyParts => [
  ...adminTenantsPrefix,
  {
    search,
    kind,
    status,
    isLive,
    page,
    take,
  },
];

export const adminTenantKey = (id: string | null): KeyParts => ['admin', 'tenant', id];

// An empty filter is left out of the request, not sent as an empty string.
function listQuery({
  search,
  kind,
  status,
  isLive,
  page,
  take,
}: AdminTenantsParams): Record<string, string | boolean | number> {
  const query: Record<string, string | boolean | number> = { Page: page, Take: take };

  if (search) {
    query.search = search;
  }

  if (kind) {
    query.kind = kind;
  }

  if (status) {
    query.status = status;
  }

  if (isLive !== undefined) {
    query.isLive = isLive;
  }

  return query;
}

export function useAdminTenants(
  params: MaybeRefOrGetter<AdminTenantsParams>,
): UseQueryReturnType<AdminTenantsPage, Error> {
  return useQuery({
    queryKey: computed(() => adminTenantsKey(toValue(params))),
    queryFn: async (): Promise<AdminTenantsPage> => {
      const { data, error, response } = await client.GET('/api/v{version}/admin/tenants', {
        params: { path: { version }, query: listQuery(toValue(params)) },
      });

      // A refusal is an error state, not an empty registry.
      if (error !== undefined)
        throw problemError('admin tenants', response.status, error);

      return data ?? {};
    },
    placeholderData: keepPreviousData,
  });
}

export function useAdminTenant(
  id: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<AdminTenantDetail | null, Error> {
  return useQuery({
    queryKey: computed(() => adminTenantKey(toValue(id))),
    queryFn: async (): Promise<AdminTenantDetail | null> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/tenants/{broadcasterId}',
        {
          params: { path: { version, broadcasterId: toValue(id)! } },
        },
      );

      if (error !== undefined)
        throw problemError('admin tenant', response.status, error);

      return data?.data ?? null;
    },
    enabled: computed(() => toValue(id) !== null),
  });
}

// A changed tenant changes its detail and the position of its row in every list page.
function refreshTenant(queries: QueryClient, broadcasterId: string): Promise<[void, void]> {
  return Promise.all([
    queries.invalidateQueries({ queryKey: adminTenantKey(broadcasterId) }),
    queries.invalidateQueries({ queryKey: adminTenantsPrefix }),
  ]);
}

export type SuspendInput = {
  broadcasterId: string;
  newStatus: string;
  reason: string;
};

export function useAdminSuspend() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ broadcasterId, newStatus, reason }: SuspendInput): Promise<void> => {
      const { error, response } = await client.POST('/api/v{version}/admin/tenants/{broadcasterId}/suspend', {
        params: { path: { version, broadcasterId } },
        body: { newStatus, reason },
      });

      if (error !== undefined)
        throw problemError('suspend', response.status, error);
    },
    onSuccess: (_data: void, { broadcasterId }: SuspendInput) =>
      refreshTenant(queries, broadcasterId),
  });
}

export type ReinstateInput = { broadcasterId: string; justification: string };

export function useAdminReinstate() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ broadcasterId, justification }: ReinstateInput): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/admin/tenants/{broadcasterId}/reinstate',
        {
          params: { path: { version, broadcasterId } },
          body: { justification },
        },
      );

      if (error !== undefined)
        throw problemError('reinstate', response.status, error);
    },
    onSuccess: (_data: void, { broadcasterId }: ReinstateInput) =>
      refreshTenant(queries, broadcasterId),
  });
}

export type BeginAccessInput = {
  broadcasterId: string;
  justification: string;
  breakGlass: boolean;
  expiresAt: string | null;
};

export function useAdminBeginAccess() {
  return useMutation({
    mutationFn: async ({
      broadcasterId,
      justification,
      breakGlass,
      expiresAt,
    }: BeginAccessInput): Promise<AdminAccessGrant> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/admin/tenants/{broadcasterId}/access',
        {
          params: { path: { version, broadcasterId } },
          body: {
            justification,
            breakGlass,
            expiresAt,
          },
        },
      );

      if (error !== undefined || !data?.data)
        throw problemError('begin access', response.status, error);

      return data.data;
    },
  });
}

export type ImpersonateInput = {
  userId: string;
  accessGrantId: string;
  justification: string;
};

export function useAdminImpersonate() {
  return useMutation({
    mutationFn: async ({
      userId,
      accessGrantId,
      justification,
    }: ImpersonateInput): Promise<AdminImpersonationToken> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/admin/users/{userId}/impersonate',
        {
          params: { path: { version, userId } },
          body: { accessGrantId, justification },
        },
      );

      if (error !== undefined || !data?.data)
        throw problemError('impersonate', response.status, error);

      return data.data;
    },
  });
}
