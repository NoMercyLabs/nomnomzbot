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
// The person in view is the login in ?u=, so a link opens them.
// Typing asks the bot for every known user in any channel and shows name and avatar only. Enter or a
// click opens the person: the header shows the light row at once and each part fills in as it arrives.
// A button looks a name up live on Twitch for someone the bot has never seen.
import { refDebounced } from '@vueuse/core'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter, type RouteLocationNormalizedLoaded, type Router } from 'vue-router'

import {
  TwitchUserNotFoundError,
  useCommunityMember,
  useTwitchLookup,
  useViewerSearch,
  type ViewerOption,
} from '@/api/community'
import { useCommunityList, type CommunityPage, type CommunityRow } from '@/api/people'
import ListDetail from '@/components/layout/ListDetail.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import PageTabs, { type PageTab } from '@/components/layout/PageTabs.vue'
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue'
import SearchField from '@/components/layout/SearchField.vue'
import VirtualList from '@/components/layout/VirtualList.vue'
import { Button } from '@/components/ui/button'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { usePageView } from '@/composables/usePageView'
import { usePanelState } from '@/composables/usePanelState'

import PersonDetail from './components/PersonDetail.vue'
import PersonRow from './components/PersonRow.vue'
import SearchResultRow from './components/SearchResultRow.vue'

// The light row a search hit or a Twitch lookup gives: name, avatar and login, nothing heavier.
type Seed = { id: string; login: string; displayName: string; profileImageUrl: string | null }

const SEARCH_PAUSE_MS = 250
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
const pausedSearch: Ref<string> = refDebounced(search, SEARCH_PAUSE_MS)
const typed: ComputedRef<string> = computed<string>(() => search.value.trim())
const searching: ComputedRef<boolean> = computed<boolean>(() => typed.value !== '')
const found = useViewerSearch(channelId, () => pausedSearch.value.trim())
const lookup = useTwitchLookup(channelId)
const seed: Ref<Seed | null> = ref(null)
const lookupMissing: Ref<string> = ref('')

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

const visible: ComputedRef<CommunityRow[] | undefined> = computed(() => tabRows.value)

// Hits of an older keystroke never show under newer text.
const hits: ComputedRef<ViewerOption[] | undefined> = computed(() =>
  pausedSearch.value.trim() === typed.value ? found.data.value : undefined,
)

const tabState = usePanelState(
  {
    data: visible,
    isPending: computed<boolean>(() => activeQuery.value?.isPending.value ?? false),
    isError: computed<boolean>(() => activeQuery.value?.isError.value ?? false),
  },
  (rows: CommunityRow[]) => rows.length === 0,
)
const searchState = usePanelState(
  { data: hits, isPending: found.isPending, isError: found.isError },
  (options: ViewerOption[]) => options.length === 0,
)
const listState: ComputedRef<PanelState> = computed<PanelState>(() =>
  searching.value ? searchState.value : tabState.value,
)

const emptyText: ComputedRef<string> = computed<string>(() => {
  if (view.value === 'subscriber') return t('dashboard.people.list.noSubscribers')
  if (view.value === 'regular') return t('dashboard.people.list.noRegulars')
  if (searching.value) return t('dashboard.people.noMatch', { query: typed.value })
  return t('dashboard.people.list.empty')
})

const hint: ComputedRef<string> = computed<string>(() => t(`dashboard.people.hints.${view.value}`))
const count: ComputedRef<string | undefined> = computed(() => {
  if (listState.value !== 'ready') return undefined
  const total: number = (searching.value ? hits.value : visible.value)?.length ?? 0
  return t('dashboard.people.count', { count: total }, total)
})

// The person in ?u= can sit in any loaded list (a moderator is also in All), so look in all three.
const asked: ComputedRef<string> = computed<string>(() =>
  typeof route.query.u === 'string' ? route.query.u : '',
)
const loadedRow: ComputedRef<CommunityRow | null> = computed(() => {
  const login: string = asked.value.toLowerCase()
  if (login === '') return null
  const loaded: CommunityRow[] = [everyone, moderators, vips].flatMap(
    (query: typeof everyone) => rowsOf(query) ?? [],
  )
  return loaded.find((row: CommunityRow) => row.username.toLowerCase() === login) ?? null
})

// A seeded person that is not in a loaded list: the full record is asked for only now, never while typing.
const seedInView: ComputedRef<Seed | null> = computed(() =>
  loadedRow.value === null && seed.value?.login.toLowerCase() === asked.value.toLowerCase()
    ? seed.value
    : null,
)
const member = useCommunityMember(channelId, () => seedInView.value?.id ?? null)

function lightRow(from: Seed): CommunityRow {
  return {
    id: from.id,
    internalUserId: null,
    username: from.login,
    displayName: from.displayName,
    profileImageUrl: from.profileImageUrl,
    messageCount: 0,
    watchHours: 0,
    commandsUsed: 0,
    trustLevel: 'viewer',
    isBanned: false,
    firstSeen: '',
    lastSeen: '',
  }
}

const selected: ComputedRef<CommunityRow | null> = computed(() => {
  if (loadedRow.value) return loadedRow.value
  if (seedInView.value === null) return null
  return member.data.value ?? lightRow(seedInView.value)
})

const detailState: ComputedRef<PanelState> = computed<PanelState>(() => {
  if (seedInView.value === null) return 'ready'
  if (member.isError.value) return 'error'
  return member.data.value ? 'ready' : 'loading'
})

function pick(row: CommunityRow): void {
  void router.push({ query: { ...route.query, u: row.username } })
}

function open(from: Seed): void {
  seed.value = from
  void router.push({ query: { ...route.query, u: from.login } })
}

function openOption(option: ViewerOption): void {
  open({
    id: option.id,
    login: option.subLabel.replace(/^@/, ''),
    displayName: option.label,
    profileImageUrl: option.imageUrl,
  })
}

function openFirstHit(): void {
  const first: ViewerOption | undefined = hits.value?.[0]
  if (first) openOption(first)
}

async function lookUp(): Promise<void> {
  const login: string = typed.value.replace(/^@/, '')
  lookupMissing.value = ''
  try {
    const person = await lookup.mutateAsync(login)
    open({
      id: person.id,
      login: person.login,
      displayName: person.displayName,
      profileImageUrl: person.profileImageUrl,
    })
  } catch (error: unknown) {
    if (error instanceof TwitchUserNotFoundError) lookupMissing.value = login
  }
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
        <SearchField
          v-model="search"
          class="w-full"
          :placeholder="$t('dashboard.people.search')"
          @keydown.enter="openFirstHit"
        />
        <p class="text-sm text-muted-foreground">{{ hint }}</p>
      </div>
      <PanelCard
        class="min-h-80 flex-1"
        :title="$t('dashboard.people.list.title')"
        :description="count"
        :state="listState"
        :empty-text="emptyText"
        :error-text="$t('dashboard.people.list.error')"
        @retry="searching ? found.refetch() : activeQuery?.refetch()"
      >
        <div v-if="searching" class="grid gap-0.5">
          <SearchResultRow
            v-for="option in hits ?? []"
            :key="option.id"
            :option="option"
            :selected="option.subLabel.replace(/^@/, '').toLowerCase() === asked.toLowerCase()"
            @pick="openOption(option)"
          />
        </div>
        <VirtualList
          v-else
          class="h-full"
          :items="visible ?? []"
          :item-key="(row: CommunityRow) => row.id"
          :estimate-size="ROW_HEIGHT"
          :label="$t('dashboard.people.list.label')"
        >
          <template #default="{ item }">
            <PersonRow
              :person="item"
              :selected="item.username.toLowerCase() === asked.toLowerCase()"
              @pick="pick(item)"
            />
          </template>
        </VirtualList>
        <template v-if="searching" #footer>
          <Button variant="outline" size="sm" :disabled="lookup.isPending.value" @click="lookUp">
            {{ $t('dashboard.people.lookup.button') }}
          </Button>
          <p v-if="lookupMissing !== ''" role="alert" class="text-sm text-muted-foreground">
            {{ $t('dashboard.people.lookup.notFound', { login: lookupMissing }) }}
          </p>
          <p v-else-if="lookup.isError.value" role="alert" class="text-sm text-destructive">
            {{ $t('dashboard.people.lookup.error') }}
          </p>
        </template>
        <template v-else-if="activeQuery?.hasNextPage.value" #footer>
          <Button
            variant="outline"
            size="sm"
            :disabled="activeQuery.isFetchingNextPage.value"
            @click="activeQuery.fetchNextPage()"
          >
            {{ $t('dashboard.people.list.more') }}
          </Button>
        </template>
      </PanelCard>
    </template>
    <template #detail>
      <PersonDetail
        v-if="selected"
        :person="selected"
        :detail-state="detailState"
        @back="back"
        @retry="member.refetch()"
      />
      <PanelCard
        v-else
        class="flex-1"
        :title="$t('dashboard.people.detail.title')"
        state="empty"
        :empty-text="
          asked === ''
            ? $t('dashboard.people.detail.pick')
            : $t('dashboard.people.detail.notFound', { login: asked })
        "
      />
    </template>
  </ListDetail>
</template>
