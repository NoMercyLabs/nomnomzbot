// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useLocalStorage, type RemovableRef } from '@vueuse/core'
import { defineStore } from 'pinia'
import { computed, type ComputedRef } from 'vue'

import { merge } from '@/lib/array'
import { officialOrigin, servedOrigin } from '@/lib/botAddress'

const alwaysListed: readonly string[] = [servedOrigin, officialOrigin]

function readOrigins(raw: string): string[] {
  try {
    const parsed: unknown = JSON.parse(raw)

    return Array.isArray(parsed) && parsed.every((item) => typeof item === 'string')
      ? parsed
      : []
  } catch {
    return []
  }
}

// The bots this browser has reached, so the visitor can go back without typing the address again.
export const useConnectionsStore = defineStore('connections', () => {
  const saved: RemovableRef<string[]> = useLocalStorage<string[]>('nnz.bots', [], {
    serializer: { read: readOrigins, write: (origins) => JSON.stringify(origins) },
  })

  const bots: ComputedRef<string[]> = computed(() => [
    ...new Set(merge(alwaysListed, saved.value)),
  ])

  function isAlwaysListed(origin: string): boolean {
    return alwaysListed.includes(origin)
  }

  function remember(origin: string): void {
    if (isAlwaysListed(origin)) return

    saved.value = [origin, ...saved.value.filter((known) => known !== origin)]
  }

  function forget(origin: string): void {
    saved.value = saved.value.filter((known) => known !== origin)
  }

  return { bots, isAlwaysListed, remember, forget }
})
