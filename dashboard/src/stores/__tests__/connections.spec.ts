// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import { beforeEach, describe, expect, it } from 'vitest'

import { useConnectionsStore } from '../connections'

const served = window.location.origin
const official: string = 'https://nomnomz.bot'

beforeEach(() => {
  localStorage.clear()
  setActivePinia(createPinia())
})

describe('saved bots', () => {
  it('starts with the bot that serves this page and the official bot', () => {
    expect(useConnectionsStore().bots).toEqual([served, official])
  })

  it('lists the official bot once, even when the visitor saved it too', () => {
    const connections = useConnectionsStore()

    connections.remember(official)

    expect(connections.bots).toEqual([served, official])
  })

  it('remembers a bot once, newest first, and keeps it after a reload', async () => {
    const connections = useConnectionsStore()

    connections.remember('https://a.example.com')
    connections.remember('http://192.168.1.20:5080')
    connections.remember('https://a.example.com')
    await nextTick()

    setActivePinia(createPinia())

    expect(useConnectionsStore().bots).toEqual([
      served,
      official,
      'https://a.example.com',
      'http://192.168.1.20:5080',
    ])
  })

  it('forgets a saved bot but never the serving or the official one', async () => {
    const connections = useConnectionsStore()

    connections.remember('https://a.example.com')
    connections.forget('https://a.example.com')
    connections.forget(served)
    connections.forget(official)
    await nextTick()

    expect(connections.bots).toEqual([served, official])
    expect(JSON.parse(localStorage.getItem('nnz.bots') ?? 'null')).toEqual([])
  })

  it('ignores a stored list that is not a list of addresses', () => {
    localStorage.setItem('nnz.bots', '{"broken":true}')

    expect(useConnectionsStore().bots).toEqual([served, official])
  })
})
