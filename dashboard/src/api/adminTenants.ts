// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useMutation, useQuery, useQueryClient, type UseQueryReturnType } from '@tanstack/vue-query';
import { computed, toValue, type MaybeRefOrGetter } from 'vue';

import { client } from './client';
import { problemError } from './errors';
import type { components } from './schema';
import type { KeyParts } from '@/lib/array';

export type TenantMember = components['schemas']['TenantMember'];
export type TenantMembersPage = components['schemas']['PaginatedResponseOfTenantMember'];
export type TenantLimit = components['schemas']['TenantLimitOverride'];
export type TenantRemigration = components['schemas']['TenantRemigrationResult'];
export type TenantErasePreview = components['schemas']['ChannelDeletePreview'];

const version = '1';

export const tenantMembersKey = (id: string | null, search: string): KeyParts => [
  'admin',
  'tenant',
  id,
  'members',
  search,
];

export const tenantLimitsKey = (id: string | null): KeyParts => ['admin', 'tenant', id, 'limits'];

export const tenantErasePreviewKey = (id: string | null): KeyParts => [
  'admin',
  'tenant',
  id,
  'erase-preview',
];

// The people of one tenant: owner, management members, community members and seen viewers.
export function useTenantMembers(
  id: MaybeRefOrGetter<string | null>,
  search: MaybeRefOrGetter<string>,
): UseQueryReturnType<TenantMembersPage, Error> {
  return useQuery({
    queryKey: computed(() => tenantMembersKey(toValue(id), toValue(search))),
    queryFn: async (): Promise<TenantMembersPage> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/tenants/{broadcasterId}/members',
        {
          params: {
            path: { version, broadcasterId: toValue(id)! },
            query: {
              search: toValue(search) || undefined,
              Page: 1,
              Take: 50,
            },
          },
        },
      );

      if (error !== undefined)
        throw problemError('tenant members', response.status, error);

      return data ?? {};
    },
    enabled: computed(() => toValue(id) !== null),
  });
}

// Every live quota override of one tenant.
export function useTenantLimits(
  id: MaybeRefOrGetter<string | null>,
): UseQueryReturnType<TenantLimit[], Error> {
  return useQuery({
    queryKey: computed(() => tenantLimitsKey(toValue(id))),
    queryFn: async (): Promise<TenantLimit[]> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/tenants/{broadcasterId}/limits',
        { params: { path: { version, broadcasterId: toValue(id)! } } },
      );

      if (error !== undefined)
        throw problemError('tenant limits', response.status, error);

      return data?.data ?? [];
    },
    enabled: computed(() => toValue(id) !== null),
  });
}

export type SetLimitInput = {
  broadcasterId: string;
  limitKey: string;
  limitValue: number;
  reason: string;
  expiresAt: string | null;
};

export function useSetTenantLimit() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ broadcasterId, ...body }: SetLimitInput): Promise<void> => {
      const { error, response } = await client.PUT(
        '/api/v{version}/admin/tenants/{broadcasterId}/limits',
        { params: { path: { version, broadcasterId } }, body },
      );

      if (error !== undefined)
        throw problemError('tenant limit save', response.status, error);
    },
    onSuccess: (_data: void, { broadcasterId }: SetLimitInput) =>
      queries.invalidateQueries({ queryKey: tenantLimitsKey(broadcasterId) }),
  });
}

export type ClearLimitInput = { broadcasterId: string; limitKey: string };

export function useClearTenantLimit() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ broadcasterId, limitKey }: ClearLimitInput): Promise<void> => {
      const { error, response } = await client.DELETE(
        '/api/v{version}/admin/tenants/{broadcasterId}/limits/{limitKey}',
        { params: { path: {
          version,
          broadcasterId,
          limitKey,
        } } },
      );

      if (error !== undefined)
        throw problemError('tenant limit clear', response.status, error);
    },
    onSuccess: (_data: void, { broadcasterId }: ClearLimitInput) =>
      queries.invalidateQueries({ queryKey: tenantLimitsKey(broadcasterId) }),
  });
}

export type JustifiedInput = { broadcasterId: string; justification: string };

export function useRemigrateTenant() {
  return useMutation({
    mutationFn: async ({
      broadcasterId,
      justification,
    }: JustifiedInput): Promise<TenantRemigration> => {
      const { data, error, response } = await client.POST(
        '/api/v{version}/admin/tenants/{broadcasterId}/remigrate',
        { params: { path: { version, broadcasterId } }, body: { justification } },
      );

      if (error !== undefined || !data?.data)
        throw problemError('tenant remigrate', response.status, error);

      return data.data;
    },
  });
}

// What an erase would hit; read only, so it is safe to load when the dialog opens.
export function useTenantErasePreview(
  id: MaybeRefOrGetter<string | null>,
  enabled: MaybeRefOrGetter<boolean>,
): UseQueryReturnType<TenantErasePreview | null, Error> {
  return useQuery({
    queryKey: computed(() => tenantErasePreviewKey(toValue(id))),
    queryFn: async (): Promise<TenantErasePreview | null> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/tenants/{broadcasterId}/erase/preview',
        { params: { path: { version, broadcasterId: toValue(id)! } } },
      );

      if (error !== undefined)
        throw problemError('tenant erase preview', response.status, error);

      return data?.data ?? null;
    },
    enabled: computed(() => toValue(id) !== null && toValue(enabled)),
  });
}

export function useEraseTenant() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async ({ broadcasterId, justification }: JustifiedInput): Promise<void> => {
      const { error, response } = await client.POST(
        '/api/v{version}/admin/tenants/{broadcasterId}/erase',
        { params: { path: { version, broadcasterId } }, body: { justification } },
      );

      if (error !== undefined)
        throw problemError('tenant erase', response.status, error);
    },
    onSuccess: () => queries.invalidateQueries({ queryKey: ['admin'] }),
  });
}

// The export is a text document; the page hands it to the browser as a file.
export function useExportTenant() {
  return useMutation({
    mutationFn: async (broadcasterId: string): Promise<string> => {
      const { data, error, response } = await client.GET(
        '/api/v{version}/admin/tenants/{broadcasterId}/export',
        { params: { path: { version, broadcasterId } } },
      );

      const exported = data?.data;

      if (error !== undefined || exported === undefined || exported === null)
        throw problemError('tenant export', response.status, error);

      return exported;
    },
  });
}

export type SupportContact = components['schemas']['SupportContact'];

export const supportContactKey: KeyParts = ['admin', 'support-contact'];

// The page a suspended channel's people use to reach the platform. Null until it is set.
export function useSupportContact(): UseQueryReturnType<SupportContact, Error> {
  return useQuery({
    queryKey: supportContactKey,
    queryFn: async (): Promise<SupportContact> => {
      const { data, error, response } = await client.GET('/api/v{version}/admin/support-contact', {
        params: { path: { version } },
      });

      if (error !== undefined || !data?.data)
        throw problemError('support contact', response.status, error);

      return data.data;
    },
  });
}

// An empty address clears the contact; the bot refuses an address that is not an absolute http or https link.
export function useSaveSupportContact() {
  const queries = useQueryClient();

  return useMutation({
    mutationFn: async (url: string): Promise<SupportContact> => {
      const { data, error, response } = await client.PUT('/api/v{version}/admin/support-contact', {
        params: { path: { version } },
        body: { url },
      });

      if (error !== undefined || !data?.data)
        throw problemError('save support contact', response.status, error);

      return data.data;
    },
    onSuccess: (saved: SupportContact) => {
      queries.setQueryData(supportContactKey, saved);
    },
  });
}
