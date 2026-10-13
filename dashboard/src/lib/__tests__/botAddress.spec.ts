// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { describe, expect, it } from 'vitest'

import { isMixedContent, toBotOrigin } from '../botAddress'

describe('toBotOrigin', () => {
  it.each([
    ['  https://bot.example.com/  ', 'https://bot.example.com'],
    ['https://bot.example.com/app/login?x=1', 'https://bot.example.com'],
    ['bot.example.com', 'https://bot.example.com'],
    ['localhost:5080', 'http://localhost:5080'],
    ['192.168.1.20:5080', 'http://192.168.1.20:5080'],
    ['10.0.0.5', 'http://10.0.0.5'],
    ['100.101.102.103:5080', 'http://100.101.102.103:5080'],
    ['[fd12:3456::1]:5080', 'http://[fd12:3456::1]:5080'],
    ['8.8.8.8', 'https://8.8.8.8'],
    ['nomnomz.local:5080', 'http://nomnomz.local:5080'],
    ['homeserver:5080', 'http://homeserver:5080'],
    ['HTTP://LocalHost:5080', 'http://localhost:5080'],
  ])('turns %j into %j', (typed, origin) => {
    expect(toBotOrigin(typed)).toBe(origin)
  })

  it.each(['', '   ', 'ftp://bot.example.com', 'http://', 'not a host'])('refuses %j', (typed) => {
    expect(toBotOrigin(typed)).toBeNull()
  })
})

describe('isMixedContent', () => {
  it('blocks a plain http bot from an https page', () => {
    expect(isMixedContent('https://bot.example.com', 'http://192.168.1.20:5080')).toBe(true)
  })

  it('allows the loopback bot, which browsers treat as secure', () => {
    expect(isMixedContent('https://bot.example.com', 'http://localhost:5080')).toBe(false)
    expect(isMixedContent('https://bot.example.com', 'http://127.0.0.1:5080')).toBe(false)
  })

  it('allows any bot from a plain http page', () => {
    expect(isMixedContent('http://localhost:5090', 'http://192.168.1.20:5080')).toBe(false)
  })
})
