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
import { computed, watch, type ComputedRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'

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

// Which groups are open: group key to true (open) or false (folded). It outlives a reload. Only the person folds a group.
const folds: RemovableRef<Record<string, boolean>> = useStorage<Record<string, boolean>>(
  'nnz.nav.folds',
  {},
)

export function useNavigation(): Navigation {
  const router = useRouter()
  const route = useRoute()

  // A page that is not built yet has no route, so it has no entry (pages appear as they land).
  function isCurrent(entry: NavEntry): boolean {
    return (
      typeof route.name === 'string' &&
      (route.name === entry.route || route.name.startsWith(`${entry.route}-`))
    )
  }

  const groups = computed<VisibleGroup[]>(() =>
    dashboardNav
      .map((group: NavGroup): VisibleGroup => {
        const entries = group.entries.filter((entry) => router.hasRoute(entry.route))
        const holdsOpenPage = entries.some(isCurrent)

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

  // Opening a page opens its group and remembers that, so moving on to another group never folds it.
  watch(
    () => groups.value.find((group) => group.foldable && group.holdsOpenPage)?.key,
    (key: string | undefined) => {
      if (key !== undefined && folds.value[key] !== true)
        folds.value = { ...folds.value, [key]: true }
    },
    { immediate: true },
  )

  function toggle(group: VisibleGroup): void {
    if (!group.foldable || group.holdsOpenPage) return
    folds.value = { ...folds.value, [group.key]: !group.open }
  }

  return {
    groups,
    entries: computed(() => groups.value.flatMap((group) => group.entries)),
    isCurrent,
    toggle,
  }
}
