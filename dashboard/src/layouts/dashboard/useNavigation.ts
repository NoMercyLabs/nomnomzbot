// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useStorage, type RemovableRef } from '@vueuse/core'
import { computed, type ComputedRef } from 'vue'
import { useRoute, useRouter, type RouteLocationNormalizedLoaded, type Router } from 'vue-router'

import { dashboardNav, type NavEntry, type NavGroup } from './navigation'

export type VisibleGroup = {
  key: string
  entries: NavEntry[]
  // A group of one page has nothing to fold.
  foldable: boolean
  open: boolean
  holdsOpenPage: boolean
}

export type Navigation = {
  groups: ComputedRef<VisibleGroup[]>
  entries: ComputedRef<NavEntry[]>
  isCurrent: (entry: NavEntry) => boolean
  toggle: (group: VisibleGroup) => void
}

// Which groups the person opened: group key to true (open) or false (folded). It outlives a reload.
const folds: RemovableRef<Record<string, boolean>> = useStorage<Record<string, boolean>>('nnz.nav.folds', {})

export function useNavigation(): Navigation {
  const router: Router = useRouter()
  const route: RouteLocationNormalizedLoaded = useRoute()

  // A page that is not built yet has no route, so it has no entry (pages appear as they land).
  function isCurrent(entry: NavEntry): boolean {
    return typeof route.name === 'string' && (route.name === entry.route || route.name.startsWith(`${entry.route}-`))
  }

  const groups = computed<VisibleGroup[]>(() =>
    dashboardNav
      .map((group: NavGroup): VisibleGroup => {
        const entries: NavEntry[] = group.entries.filter((entry) => router.hasRoute(entry.route))
        const holdsOpenPage: boolean = entries.some(isCurrent)
        return {
          key: group.key,
          entries,
          foldable: entries.length > 1,
          open: entries.length <= 1 || holdsOpenPage || folds.value[group.key] === true,
          holdsOpenPage,
        }
      })
      .filter((group) => group.entries.length > 0),
  )

  function toggle(group: VisibleGroup): void {
    if (!group.foldable || group.holdsOpenPage) return
    folds.value = { ...folds.value, [group.key]: !group.open }
  }

  return { groups, entries: computed(() => groups.value.flatMap((group) => group.entries)), isCurrent, toggle }
}
