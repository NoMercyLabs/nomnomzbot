<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The People page: a list of everyone the bot has seen, tabs by role, and one person beside it.
// Tabs without a list on the bot (Subscribers, Regulars) say so instead of drawing an empty list.
// The person in view is the login in ?u=, so a link opens them. Search filters the loaded rows.
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter, type RouteLocationNormalizedLoaded, type Router } from 'vue-router'

import { useCommunityList, type CommunityPage, type CommunityRow } from '@/api/people'
import ListDetail from '@/components/layout/ListDetail.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import PageTabs, { type PageTab } from '@/components/layout/PageTabs.vue'
import PanelCard from '@/components/layout/PanelCard.vue'
import SearchField from '@/components/layout/SearchField.vue'
import VirtualList from '@/components/layout/VirtualList.vue'
import { Button } from '@/components/ui/button'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { usePageView } from '@/composables/usePageView'
import { usePanelState } from '@/composables/usePanelState'

import PersonDetail from './components/PersonDetail.vue'
import PersonRow from './components/PersonRow.vue'

const NEW_WINDOW_DAYS = 90
const ROW_HEIGHT = 56
const TAB_VALUES: readonly string[] = ['all', 'moderator', 'vip', 'subscriber', 'regular', 'new']
const UNLISTED: readonly string[] = ['subscriber', 'regular']

const { t } = useI18n()
const route: RouteLocationNormalizedLoaded = useRoute()
const router: Router = useRouter()
const { channelId } = useCurrentChannel()

const everyone = useCommunityList(channelId, 'all', '')
const moderators = useCommunityList(channelId, 'moderator', '')
const vips = useCommunityList(channelId, 'vip', '')

const view = usePageView(TAB_VALUES)
const search: Ref<string> = ref('')

function rowsOf(query: typeof everyone): CommunityRow[] | undefined {
  return query.data.value?.pages.flatMap((page: CommunityPage) => page.rows)
}

function totalOf(query: typeof everyone): number | undefined {
  return query.data.value?.pages[0]?.total ?? undefined
}

const tabs: ComputedRef<PageTab[]> = computed<PageTab[]>(() => [
  { value: 'all', label: t('dashboard.people.tabs.all'), count: totalOf(everyone) },
  { value: 'moderator', label: t('dashboard.people.tabs.moderator'), count: totalOf(moderators) },
  { value: 'vip', label: t('dashboard.people.tabs.vip'), count: totalOf(vips) },
  { value: 'subscriber', label: t('dashboard.people.tabs.subscriber') },
  { value: 'regular', label: t('dashboard.people.tabs.regular') },
  { value: 'new', label: t('dashboard.people.tabs.new') },
])

const activeQuery: ComputedRef<typeof everyone | null> = computed(() => {
  if (UNLISTED.includes(view.value)) return null
  if (view.value === 'moderator') return moderators
  if (view.value === 'vip') return vips
  return everyone
})

function isNew(row: CommunityRow): boolean {
  return Date.now() - new Date(row.firstSeen).getTime() <= NEW_WINDOW_DAYS * 24 * 60 * 60 * 1000
}

const tabRows: ComputedRef<CommunityRow[] | undefined> = computed(() => {
  if (activeQuery.value === null) return []
  const rows: CommunityRow[] | undefined = rowsOf(activeQuery.value)
  return view.value === 'new' ? rows?.filter(isNew) : rows
})

const visible: ComputedRef<CommunityRow[] | undefined> = computed(() => {
  const typed: string = search.value.trim().toLowerCase()
  if (typed === '') return tabRows.value
  return tabRows.value?.filter(
    (row: CommunityRow) => row.displayName.toLowerCase().includes(typed) || row.username.toLowerCase().includes(typed),
  )
})

const listState = usePanelState(
  {
    data: visible,
    isPending: computed<boolean>(() => activeQuery.value?.isPending.value ?? false),
    isError: computed<boolean>(() => activeQuery.value?.isError.value ?? false),
  },
  (rows: CommunityRow[]) => rows.length === 0,
)

const emptyText: ComputedRef<string> = computed<string>(() => {
  if (view.value === 'subscriber') return t('dashboard.people.list.noSubscribers')
  if (view.value === 'regular') return t('dashboard.people.list.noRegulars')
  if (search.value.trim() !== '') return t('dashboard.people.noMatch', { query: search.value.trim() })
  return t('dashboard.people.list.empty')
})

const hint: ComputedRef<string> = computed<string>(() => t(`dashboard.people.hints.${view.value}`))
const count: ComputedRef<string | undefined> = computed(() =>
  listState.value === 'ready' ? t('dashboard.people.count', { count: visible.value?.length ?? 0 }) : undefined,
)

// The person in ?u= can sit in any loaded list (a moderator is also in All), so look in all three.
const asked: ComputedRef<string> = computed<string>(() => (typeof route.query.u === 'string' ? route.query.u : ''))
const selected: ComputedRef<CommunityRow | null> = computed(() => {
  const login: string = asked.value.toLowerCase()
  if (login === '') return null
  const loaded: CommunityRow[] = [everyone, moderators, vips].flatMap((query: typeof everyone) => rowsOf(query) ?? [])
  return loaded.find((row: CommunityRow) => row.username.toLowerCase() === login) ?? null
})

function pick(row: CommunityRow): void {
  void router.push({ query: { ...route.query, u: row.username } })
}

function back(): void {
  const { u, ...rest } = route.query
  void u
  void router.push({ query: rest })
}
</script>

<template>
  <PageHeader :title="$t('dashboard.people.title')" :crumb="$t('dashboard.people.crumb')">
    <template #tabs>
      <PageTabs :tabs="tabs" />
    </template>
  </PageHeader>
  <ListDetail :show-detail="asked !== ''">
    <template #list>
      <div class="mb-3 grid gap-2">
        <SearchField v-model="search" class="w-full" :placeholder="$t('dashboard.people.search')" />
        <p class="text-sm text-muted-foreground">{{ hint }}</p>
      </div>
      <PanelCard
        class="min-h-80 flex-1"
        :title="$t('dashboard.people.list.title')"
        :description="count"
        :state="listState"
        :empty-text="emptyText"
        :error-text="$t('dashboard.people.list.error')"
        @retry="activeQuery?.refetch()"
      >
        <VirtualList
          class="h-full"
          :items="visible ?? []"
          :item-key="(row: CommunityRow) => row.id"
          :estimate-size="ROW_HEIGHT"
          :label="$t('dashboard.people.list.label')"
        >
          <template #default="{ item }">
            <PersonRow :person="item" :selected="item.username.toLowerCase() === asked.toLowerCase()" @pick="pick(item)" />
          </template>
        </VirtualList>
        <template v-if="activeQuery?.hasNextPage.value" #footer>
          <Button variant="outline" size="sm" :disabled="activeQuery.isFetchingNextPage.value" @click="activeQuery.fetchNextPage()">
            {{ $t('dashboard.people.list.more') }}
          </Button>
        </template>
      </PanelCard>
    </template>
    <template #detail>
      <PersonDetail v-if="selected" :person="selected" @back="back" />
      <PanelCard
        v-else
        class="flex-1"
        :title="$t('dashboard.people.detail.title')"
        state="empty"
        :empty-text="
          asked === '' ? $t('dashboard.people.detail.pick') : $t('dashboard.people.detail.notFound', { login: asked })
        "
      />
    </template>
  </ListDetail>
</template>
