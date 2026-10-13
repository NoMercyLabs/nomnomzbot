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
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
  type QueryClient,
  type UseMutationReturnType,
  type UseQueryReturnType,
} from '@tanstack/vue-query'
import { computed, toValue, type MaybeRefOrGetter } from 'vue'

import { client } from './client'
import type { components } from './schema'

export type AdminTenant = components['schemas']['AdminTenantDto']
export type AdminTenantDetail = components['schemas']['AdminTenantDetailDto']
export type AdminTenantsPage = components['schemas']['PaginatedResponseOfAdminTenantDto']
export type AdminAccessGrant = components['schemas']['TenantAccessGrantDto']
export type AdminImpersonationToken = components['schemas']['ImpersonationTokenDto']

export type AdminTenantsParams = {
  search?: string
  status?: string
  isLive?: boolean
  page: number
  take: number
}

const version = '1'

export const adminSystemStatusKey: unknown[] = ['admin', 'system-status']

// Every cached list page starts with this prefix, whatever its filters.
export const adminTenantsPrefix: unknown[] = ['admin', 'tenants']

export const adminTenantsKey = ({
  search,
  status,
  isLive,
  page,
  take,
}: AdminTenantsParams): unknown[] => [...adminTenantsPrefix, { search, status, isLive, page, take }]

export const adminTenantKey = (id: string | null): unknown[] => ['admin', 'tenant', id]

// An empty filter is left out of the request, not sent as an empty string.
function listQuery({
  search,
  status,
  isLive,
  page,
  take,
}: AdminTenantsParams): Record<string, string | boolean | number> {
  const query: Record<string, string | boolean | number> = { Page: page, Take: take }

  if (search) {
    query.search = search
  }

  if (status) {
    query.status = status
  }

  if (isLive !== undefined) {
    query.isLive = isLive
  }

  return query
}

export function useAdminTenants(
  params: MaybeRefOrGetter<AdminTenantsParams>,
): UseQueryReturnType<AdminTenantsPage, Error> {
  return useQuery({
    queryKey: computed(() => adminTenantsKey(toValue(params))),
    queryFn: async (): Promise<AdminTenantsPage> => {
      const { data, error, response } = await client.GET('/api/v{version}/admin/tenants', {
        params: { path: { version }, query: listQuery(toValue(params)) },
      })

      // A refusal is an error state, not an empty registry.
      if (error !== undefined)
        throw new Error(`admin tenants ${response.status}`)

      return data ?? {}
    },
    placeholderData: keepPreviousData,
  })
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
      )

      if (error !== undefined)
        throw new Error(`admin tenant ${response.status}`)

      return data?.data ?? null
    },
    enabled: computed(() => toValue(id) !== null),
  })
}

// A changed tenant changes its detail and the position of its row in every list page.
function refreshTenant(queries: QueryClient, broadcasterId: string): Promise<unknown> {
  return Promise.all([
    queries.invalidateQueries({ queryKey: adminTenantKey(broadcasterId) }),
    queries.invalidateQueries({ queryKey: adminTenantsPrefix }),
  ])
}

export type SuspendInput = {
  broadcasterId: string
  newStatus: string
  reason: string
}

export function useAdminSuspend(): UseMutationReturnType<void, Error, SuspendInput, unknown> {
  const queries = useQueryClient()

  return useMutation({
    mutationFn: async ({ broadcasterId, newStatus, reason }: SuspendInput): Promise<void> => {
      const { error } = await client.POST('/api/v{version}/admin/tenants/{broadcasterId}/suspend', {
        params: { path: { version, broadcasterId } },
        body: { newStatus, reason },
      })

      if (error !== undefined)
        throw new Error('suspend failed')
    },
    onSuccess: (_data: void, { broadcasterId }: SuspendInput) =>
      refreshTenant(queries, broadcasterId),
  })
}

export type ReinstateInput = { broadcasterId: string; justification: string }

export function useAdminReinstate(): UseMutationReturnType<void, Error, ReinstateInput, unknown> {
  const queries = useQueryClient()

  return useMutation({
    mutationFn: async ({ broadcasterId, justification }: ReinstateInput): Promise<void> => {
      const { error } = await client.POST(
        '/api/v{version}/admin/tenants/{broadcasterId}/reinstate',
        {
          params: { path: { version, broadcasterId } },
          body: { justification },
        },
      )

      if (error !== undefined)
        throw new Error('reinstate failed')
    },
    onSuccess: (_data: void, { broadcasterId }: ReinstateInput) =>
      refreshTenant(queries, broadcasterId),
  })
}

export type BeginAccessInput = {
  broadcasterId: string
  justification: string
  breakGlass: boolean
  expiresAt: string | null
}

export function useAdminBeginAccess(): UseMutationReturnType<
  AdminAccessGrant,
  Error,
  BeginAccessInput,
  unknown
> {
  return useMutation({
    mutationFn: async ({
      broadcasterId,
      justification,
      breakGlass,
      expiresAt,
    }: BeginAccessInput): Promise<AdminAccessGrant> => {
      const { data, error } = await client.POST(
        '/api/v{version}/admin/tenants/{broadcasterId}/access',
        {
          params: { path: { version, broadcasterId } },
          body: { justification, breakGlass, expiresAt },
        },
      )

      if (error !== undefined || !data?.data)
        throw new Error('begin access failed')

      return data.data
    },
  })
}

export type ImpersonateInput = {
  userId: string
  accessGrantId: string
  justification: string
}

export function useAdminImpersonate(): UseMutationReturnType<
  AdminImpersonationToken,
  Error,
  ImpersonateInput,
  unknown
> {
  return useMutation({
    mutationFn: async ({
      userId,
      accessGrantId,
      justification,
    }: ImpersonateInput): Promise<AdminImpersonationToken> => {
      const { data, error } = await client.POST(
        '/api/v{version}/admin/users/{userId}/impersonate',
        {
          params: { path: { version, userId } },
          body: { accessGrantId, justification },
        },
      )

      if (error !== undefined || !data?.data)
        throw new Error('impersonate failed')

      return data.data
    },
  })
}
