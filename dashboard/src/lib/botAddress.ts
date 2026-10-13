// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import ipaddr from 'ipaddr.js'

// The bot that serves this page. In development VITE_API_BASE_URL points at the local bot instead.
export const servedOrigin: string = import.meta.env.VITE_API_BASE_URL ?? window.location.origin

// The bot NoMercy Labs hosts. Every client lists it, so nobody has to type its address.
export const officialOrigin: string = 'https://nomnomz.bot'

const hasScheme = /^[a-z][a-z\d+.-]*:\/\//i
const homeNetworkSuffix = /\.(local|lan|home\.arpa|internal)$/i
const homeNetworkRanges: ReadonlySet<string> = new Set([
  'loopback',
  'private',
  'linkLocal',
  'uniqueLocal',
  'carrierGradeNat',
])
const loopback: ReadonlySet<string> = new Set(['localhost', '127.0.0.1', '[::1]'])

function parse(address: string): URL | null {
  try {
    return new URL(address)
  } catch {
    return null
  }
}

// A home-network bot rarely has a certificate, so a typed address without a scheme gets http:// there.
function isHomeNetwork(hostname: string): boolean {
  const ip = hostname.replace(/^\[|\]$/g, '')

  if (ipaddr.isValid(ip)) return homeNetworkRanges.has(ipaddr.process(ip).range())

  return hostname === 'localhost' || !hostname.includes('.') || homeNetworkSuffix.test(hostname)
}

// What the visitor typed, as the bot's origin: "bot.example.com/app" becomes "https://bot.example.com".
export function toBotOrigin(typed: string): string | null {
  const address = typed.trim()

  if (!address) return null

  if (hasScheme.test(address)) {
    const url = parse(address)

    return url && (url.protocol === 'http:' || url.protocol === 'https:') && url.hostname
      ? url.origin
      : null
  }

  const probe = parse(`http://${address}`)

  if (!probe?.hostname) return null

  return isHomeNetwork(probe.hostname)
    ? probe.origin
    : (parse(`https://${address}`)?.origin ?? null)
}

// An https page may not call a plain http bot, except on this machine's loopback.
export function isMixedContent(pageOrigin: string, botOrigin: string): boolean {
  const bot = parse(botOrigin)

  return pageOrigin.startsWith('https:') && bot?.protocol === 'http:' && !loopback.has(bot.hostname)
}
