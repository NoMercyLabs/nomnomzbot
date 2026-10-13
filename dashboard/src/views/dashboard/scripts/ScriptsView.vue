<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Scripts page: every code script of the channel as a list, tabs that filter it, and a slot for one script beside it.
// The script in view is the id in ?script=. Tab counts come from the loaded list: the bot has no count endpoint.
// A refusal of the list (custom code switched off, no right) shows the bot's own reason, not the empty text.
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import Add from '~icons/nnz/add'

import { useCodeScripts, type CodeScriptSummary } from '@/api/codeScripts'
import ListDetail from '@/components/layout/ListDetail.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import PageTabs, { type PageTab } from '@/components/layout/PageTabs.vue'
import PanelCard from '@/components/layout/PanelCard.vue'
import SearchField from '@/components/layout/SearchField.vue'
import { Button } from '@/components/ui/button'
import { usePageView } from '@/composables/usePageView'
import { usePanelState } from '@/composables/usePanelState'

import { Dialog, DialogContent, DialogTitle } from '@/components/ui/dialog'

import FailedRunDialog from './components/FailedRunDialog.vue'
import NewScriptDialog from './components/NewScriptDialog.vue'
import ScriptDetail from './components/ScriptDetail.vue'
import ScriptRow from './components/ScriptRow.vue'
import ScriptRunsPanel from './components/ScriptRunsPanel.vue'
import type { SessionRun } from './components/sessionRun'

const TAB_VALUES: readonly string[] = ['all', 'on', 'off', 'failed']

const { t } = useI18n()
const route = useRoute()
const router = useRouter()

const scripts = useCodeScripts()
const view = usePageView(TAB_VALUES)
const search = ref('')

const loaded = computed(() => scripts.data.value)

const FILTERS: Readonly<Record<string, (script: CodeScriptSummary) => boolean>> = {
  all: () => true,
  on: (script: CodeScriptSummary) => script.isEnabled,
  off: (script: CodeScriptSummary) => !script.isEnabled,
  failed: (script: CodeScriptSummary) => Boolean(script.lastRuntimeError),
}

function countOf(tab: string): number | undefined {
  return loaded.value?.filter(FILTERS[tab]!).length
}

const tabs = computed<PageTab[]>(() =>
  TAB_VALUES.map((value: string): PageTab => ({
    value,
    label: t(`dashboard.scripts.tabs.${value}`),
    count: countOf(value),
  })),
)

const visible: ComputedRef<CodeScriptSummary[] | undefined> = computed(() => {
  const typed = search.value.trim().toLowerCase()

  return loaded.value
    ?.filter(FILTERS[view.value] ?? FILTERS.all!)
    .filter(
      (script: CodeScriptSummary) =>
        typed === '' ||
        script.name.toLowerCase().includes(typed) ||
        (script.description ?? '').toLowerCase().includes(typed),
    )
})

const listState = usePanelState(scripts, () =>
  false)
const panelState = computed(() => {
  if (listState.value !== 'ready')
    return listState.value

  return visible.value?.length === 0
    ? 'empty'
    : 'ready'
})

const emptyText: ComputedRef<string> = computed<string>(() => {
  if (search.value.trim() !== '')
    return t('dashboard.scripts.noMatch', { query: search.value.trim() })

  return t(
    loaded.value?.length === 0
      ? 'dashboard.scripts.list.empty'
      : 'dashboard.scripts.list.emptyTab',
  )
})
const errorText: ComputedRef<string> = computed<string>(
  () => scripts.error.value?.message || t('dashboard.scripts.list.error'),
)

const hint = computed<string>(() => t(`dashboard.scripts.hints.${view.value}`))
const count: ComputedRef<string | undefined> = computed(() =>
  panelState.value === 'ready'
    ? t('dashboard.scripts.count', { count: visible.value?.length ?? 0 })
    : undefined,
)

const asked = computed<string>(() =>
  typeof route.query.script === 'string'
    ? route.query.script
    : '',
)
const selectedId = asked

function pick(script: CodeScriptSummary): void {
  void router.push({ query: { ...route.query, script: script.id } })
}

function back(): void {
  const { script, ...rest } = route.query

  void script
  void router.push({ query: rest })
}

const newOpen = ref(false)

// The test runs of this visit, newest first. A failed run opens its dialog at once.
const runs: Ref<SessionRun[]> = ref([])
const failedRun: Ref<SessionRun | null> = ref(null)
const runsOpen = ref(false)
let nextRunId: number = 1

function ran(run: Omit<SessionRun, 'id'>): void {
  const entry: SessionRun = { ...run, id: nextRunId++ }

  runs.value = [entry, ...runs.value]

  if (!entry.result.success) {
    failedRun.value = entry
  }
}

function opened(id: string | null): void {
  if (id !== null) {
    void router.push({ query: { ...route.query, script: id } })
  }
}
</script>

<template>
  <PageHeader :title="$t('dashboard.scripts.title')" :crumb="$t('dashboard.scripts.crumb')">
    <template #actions>
      <Button variant="outline" class="xl:hidden" @click="runsOpen = true">
        {{ $t('dashboard.scripts.runs.button') }}
      </Button>
      <Button variant="outline" @click="newOpen = true">
        <Add aria-hidden="true" />
        {{ $t('dashboard.scripts.new.button') }}
      </Button>
    </template>
    <template #tabs>
      <PageTabs :tabs="tabs" />
    </template>
  </PageHeader>
  <NewScriptDialog v-model:open="newOpen" @created="opened" />
  <ListDetail :show-detail="asked !== ''">
    <template #list>
      <div class="mb-3 grid gap-2">
        <SearchField
          v-model="search"
          class="w-full"
          :placeholder="$t('dashboard.scripts.search')"
        />
        <p class="text-sm text-muted-foreground">{{ hint }}</p>
      </div>
      <PanelCard
        class="min-h-80 flex-1"
        :title="$t('dashboard.scripts.list.title')"
        :description="count"
        :state="panelState"
        :empty-text="emptyText"
        :error-text="errorText"
        @retry="scripts.refetch()"
      >
        <ScriptRow
          v-for="script in visible"
          :key="script.id"
          :script="script"
          :selected="script.id === selectedId"
          @pick="pick(script)"
        />
      </PanelCard>
    </template>
    <template #detail>
      <ScriptDetail
        v-if="asked !== ''"
        :key="asked"
        :script-id="asked"
        :summary="loaded?.find((one: CodeScriptSummary) => one.id === asked)"
        @back="back"
        @deleted="back"
        @ran="ran"
      />
      <PanelCard
        v-else
        class="flex-1"
        :title="$t('dashboard.scripts.detail.title')"
        state="empty"
        :empty-text="$t('dashboard.scripts.detail.pick')"
      />
    </template>
    <template #aside>
      <ScriptRunsPanel :runs="runs" @open="failedRun = $event" />
    </template>
  </ListDetail>
  <FailedRunDialog :run="failedRun" @close="failedRun = null" />
  <Dialog v-model:open="runsOpen">
    <DialogContent :show-close-button="false" class="flex max-h-[90vh] flex-col p-0 sm:max-w-115">
      <DialogTitle class="sr-only">{{ $t('dashboard.scripts.runs.title') }}</DialogTitle>
      <ScriptRunsPanel :runs="runs" @open="failedRun = $event" />
      <Button variant="outline" class="m-2" @click="runsOpen = false">
        {{ $t('dashboard.scripts.runs.close') }}
      </Button>
    </DialogContent>
  </Dialog>
</template>
