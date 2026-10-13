// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { computed, toValue, type MaybeRefOrGetter, type WritableComputedRef } from 'vue'
import { useRoute, useRouter, type RouteLocationNormalizedLoaded, type Router } from 'vue-router'

// The open tab of a page lives in the route query (?view=), so a link opens a tab and Back returns to the last one.
// A missing or unknown value falls back to the first tab; the query stays clean until a person picks another tab.
export function usePageView(values: MaybeRefOrGetter<readonly string[]>): WritableComputedRef<string> {
  const route: RouteLocationNormalizedLoaded = useRoute()
  const router: Router = useRouter()

  return computed<string>({
    get: (): string => {
      const known: readonly string[] = toValue(values)
      const asked: unknown = route.query.view
      return typeof asked === 'string' && known.includes(asked) ? asked : (known[0] ?? '')
    },
    set: (view: string): void => {
      void router.push({ query: { ...route.query, view } })
    },
  })
}
