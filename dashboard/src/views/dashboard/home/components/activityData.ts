// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The activity feed carries its readable detail in a JSON string. Two writers fill it with
// different key names (the journal projection and the alert handlers), so each reader takes
// a list of keys and uses the first one that holds a value.

export type ActivityData = Record<string, unknown>

export function parseActivityData(raw: string | null | undefined): ActivityData {
  if (raw === null || raw === undefined)
    return {}

  try {
    const parsed: unknown = JSON.parse(raw)

    return typeof parsed === 'object' && parsed !== null && !Array.isArray(parsed)
      ? (parsed as ActivityData)
      : {}
  } catch {
    return {}
  }
}

export function pickText(data: ActivityData, ...keys: string[]): string | null {
  for (const key of keys) {
    const value: unknown = data[key]

    if (typeof value === 'string' && value.trim() !== '')
      return value
  }

  return null
}

export function pickCount(data: ActivityData, ...keys: string[]): number | null {
  for (const key of keys) {
    const value: unknown = data[key]
    const number =
      typeof value === 'string' && value.trim() !== ''
        ? Number(value)
        : (value as number)

    if (typeof number === 'number' && Number.isFinite(number))
      return number
  }

  return null
}

// "1000", "Tier 2", "3" and "Prime" all become one label: the tier number, or "Prime".
export function tierOf(raw: unknown): string | null {
  if (typeof raw === 'number')
    return String(raw >= 1000
      ? raw / 1000
      : raw)

  if (typeof raw !== 'string')
    return null

  if (/prime/i.test(raw))
    return 'Prime'

  const thousands = raw.match(/(\d)000/)

  if (thousands)
    return thousands[1] as string

  const digit = raw.match(/\d/)

  return digit
    ? digit[0]
    : null
}
