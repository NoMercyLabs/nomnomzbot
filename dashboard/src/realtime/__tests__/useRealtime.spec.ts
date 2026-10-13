// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { flushPromises } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, effectScope, nextTick, ref, type EffectScope, type Ref } from 'vue'

import { FakeHubConnection, FakeHubConnectionBuilder } from './fakeSignalR'

vi.mock('@microsoft/signalr', () =>
  ({
    HubConnectionBuilder: FakeHubConnectionBuilder,
    HubConnection: FakeHubConnection,
  }))

const signedIn = ref(false)
const channel: Ref<string | null> = ref('c1')
const admin = ref(false)

vi.mock('@/api/auth', () =>
  ({
    useSignedIn: () => signedIn,
    getAccessToken: () => 'token',
    useCurrentUser: () => ({ data: computed(() => ({ isAdmin: admin.value })) }),
  }))
vi.mock('@/composables/useCurrentChannel', () =>
  ({
    useCurrentChannel: () => ({ channelId: computed(() => channel.value) }),
  }))

type Status = (typeof import('../hubStatus'))['dashboardHubStatus']

// Modules are loaded again for each test, so the status ref comes from the same load as the hooks.
let dashboardHubStatus: Status
let scope: EffectScope | null = null

async function mounted(): Promise<void> {
  vi.resetModules()
  const { useRealtime } = await import('../useRealtime')

  dashboardHubStatus = (await import('../hubStatus')).dashboardHubStatus
  scope = effectScope()
  scope.run(() => useRealtime())
  await flushPromises()
}

afterEach(() => {
  scope?.stop()
})

beforeEach(() => {
  FakeHubConnection.instances = []
  signedIn.value = false
  channel.value = 'c1'
  admin.value = false
})

describe('useRealtime', () => {
  it('opens no connection while signed out', async () => {
    await mounted()
    expect(FakeHubConnection.instances).toHaveLength(0)
  })

  it('connects to the dashboard hub at sign-in and joins the current channel', async () => {
    await mounted()
    signedIn.value = true
    await flushPromises()

    expect(FakeHubConnection.instances.map((c) => new URL(c.url).pathname)).toEqual([
      '/hubs/dashboard',
    ])
    expect(FakeHubConnection.at(0).invocations).toEqual([['JoinChannel', 'c1']])
    expect(dashboardHubStatus.value).toBe('connected')
  })

  it('sign-out stops the connection', async () => {
    await mounted()
    signedIn.value = true
    await flushPromises()
    signedIn.value = false
    await flushPromises()

    expect(FakeHubConnection.at(0).stopped).toBe(true)
    expect(dashboardHubStatus.value).toBe('offline')
  })

  it('a channel change leaves the old group and joins the new one', async () => {
    await mounted()
    signedIn.value = true
    await flushPromises()
    channel.value = 'c2'
    await nextTick()
    await flushPromises()

    expect(FakeHubConnection.at(0).invocations).toEqual([
      ['JoinChannel', 'c1'],
      ['LeaveChannel', 'c1'],
      ['JoinChannel', 'c2'],
    ])
  })

  it('opens the operator hub only for an operator', async () => {
    await mounted()
    signedIn.value = true
    admin.value = true
    await flushPromises()

    expect(FakeHubConnection.instances.map((c) => new URL(c.url).pathname).sort()).toEqual([
      '/hubs/admin',
      '/hubs/dashboard',
    ])
  })
})
