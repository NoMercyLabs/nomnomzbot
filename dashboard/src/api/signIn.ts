// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { queryOptions, skipToken } from '@tanstack/vue-query'

import type { components } from './schema'
import { activeBot } from './activeBot'
import { acceptAccessToken } from './auth'
import { client } from './client'

export type LoginProvider = components['schemas']['LoginProviderDto']
export type DeviceCode = components['schemas']['DeviceCodeStartDto']
export type DeviceOutcome = 'authorized' | 'expired' | 'denied' | 'error'
export type DeviceApproval = { status: DeviceOutcome | 'pending' | 'slow_down'; intervalMs: number }

const approvalStatuses: ReadonlySet<string> = new Set(['pending', 'slow_down', 'authorized', 'expired', 'denied'])

// The bot may answer with a status this client does not know yet; that ends the wait as an error.
function toApprovalStatus(status: string): DeviceApproval['status'] {
  return approvalStatuses.has(status) ? (status as DeviceApproval['status']) : 'error'
}
const version = { version: '1' }
const slowDownMs: number = 5000

async function fetchProviders(): Promise<LoginProvider[]> {
  const { data } = await client.GET('/api/v{version}/auth/providers', { params: { path: version } })
  return data?.data ?? []
}

export const providersQuery = queryOptions({ queryKey: ['auth', 'providers'], queryFn: fetchProviders })

// Twitch has its own route; every other provider goes through the shared PKCE authorize route.
export function signInUrl(provider: string, returnTo: string): string {
  const query = new URLSearchParams({ client: 'web', return_to: returnTo })
  const path: string = provider === 'twitch' ? 'auth/twitch' : `auth/${encodeURIComponent(provider)}/authorize`
  return `${activeBot.value}/api/v1/${path}?${query}`
}

export async function startDeviceSignIn(provider: string): Promise<DeviceCode | null> {
  const { data } = await client.POST('/api/v{version}/auth/{provider}/device', {
    params: { path: { ...version, provider } },
  })
  return data?.data ?? null
}

async function pollOnce(provider: string, deviceCode: string): Promise<string> {
  const { data } = await client.POST('/api/v{version}/auth/{provider}/device/poll', {
    params: { path: { ...version, provider }, query: { client: 'web' } },
    body: { deviceCode },
    credentials: 'include',
  })
  const poll = data?.data
  if (poll?.status === 'authorized' && poll.auth) acceptAccessToken(poll.auth.accessToken)
  return poll?.status ?? 'error'
}

function isWaiting(approval: DeviceApproval | undefined): boolean {
  return approval?.status === 'pending' || approval?.status === 'slow_down'
}

// Polls until the visitor approves the code on the provider's site, or it ends another way.
// The query stops by itself when the code panel unmounts, and waits while there is no code yet.
export function deviceApprovalQuery(provider: string, code: DeviceCode | null) {
  const queryKey = ['auth', 'device', provider, code?.deviceCode ?? null] as const
  return queryOptions({
    queryKey,
    queryFn: code
      ? async ({ client: queries }): Promise<DeviceApproval> => {
          const previous: DeviceApproval | undefined = queries.getQueryData<DeviceApproval>(queryKey)
          const status: string = await pollOnce(provider, code.deviceCode)
          const intervalMs: number = (previous?.intervalMs ?? 0) + (status === 'slow_down' ? slowDownMs : 0)
          return { status: toApprovalStatus(status), intervalMs }
        }
      : skipToken,
    initialData: (): DeviceApproval => ({ status: 'pending', intervalMs: Number(code?.interval ?? 0) * 1000 }),
    staleTime: Infinity,
    retry: false,
    refetchOnWindowFocus: false,
    refetchIntervalInBackground: true,
    refetchInterval: (query) =>
      query.state.status !== 'error' && isWaiting(query.state.data) ? query.state.data!.intervalMs : false,
  })
}
