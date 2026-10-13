<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The test runs made on this page since it opened, newest first. The bot keeps no run table, so a reload clears them.
// A failed run opens its dialog; a run that worked is only listed.
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'

import FilterBar, { type FilterOption } from '@/components/layout/FilterBar.vue'
import PanelCard from '@/components/layout/PanelCard.vue'
import StatusBadge from '@/components/layout/StatusBadge.vue'

import { matchesRunFilter, type RunFilter, type SessionRun } from './sessionRun'

const props = defineProps<{ runs: SessionRun[] }>()
defineEmits<{ open: [run: SessionRun] }>()

const { t } = useI18n()
const filter: Ref<RunFilter> = ref<RunFilter>('all')

const options: ComputedRef<FilterOption[]> = computed<FilterOption[]>(() =>
  (['all', 'failed', 'tests'] as const).map((value: RunFilter): FilterOption => ({
    value,
    label: t(`dashboard.scripts.runs.filter.${value}`),
  })),
)
const shown: ComputedRef<SessionRun[]> = computed<SessionRun[]>(() =>
  props.runs.filter((run: SessionRun) => matchesRunFilter(run, filter.value)),
)
const state: ComputedRef<'ready' | 'empty'> = computed(() =>
  shown.value.length > 0 ? 'ready' : 'empty',
)
const emptyText: ComputedRef<string> = computed<string>(() =>
  t(props.runs.length === 0 ? 'dashboard.scripts.runs.empty' : 'dashboard.scripts.runs.noMatch'),
)

function timeOf(run: SessionRun): string {
  return run.at.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}
</script>

<template>
  <PanelCard
    data-slot="script-runs"
    class="flex-1"
    :title="$t('dashboard.scripts.runs.title')"
    :description="$t('dashboard.scripts.runs.count', { count: runs.length })"
    :state="state"
    :empty-text="emptyText"
  >
    <template #actions>
      <FilterBar
        v-model="filter"
        :label="$t('dashboard.scripts.runs.filter.label')"
        :options="options"
      />
    </template>
    <ul class="grid gap-1">
      <li v-for="run in shown" :key="run.id" data-run-row>
        <component
          :is="run.result.success ? 'div' : 'button'"
          :type="run.result.success ? undefined : 'button'"
          class="flex w-full items-center gap-3 rounded-[8px] px-3 py-2 text-left text-sm"
          :class="run.result.success ? '' : 'cursor-pointer hover:bg-foreground/5'"
          @click="run.result.success ? undefined : $emit('open', run)"
        >
          <span class="grid min-w-0 flex-1 gap-0.5">
            <StatusBadge
              :tone="run.result.success ? 'ok' : 'bad'"
              :label="
                $t(
                  run.result.success
                    ? 'dashboard.scripts.list.worked'
                    : 'dashboard.scripts.list.failed',
                )
              "
              class="font-medium"
            />
            <span data-run-name class="truncate">{{ run.scriptName }}</span>
            <span class="truncate text-xs text-muted-foreground">
              {{
                run.trigger
                  ? $t('dashboard.scripts.runs.line', { time: timeOf(run), trigger: run.trigger })
                  : timeOf(run)
              }}
            </span>
          </span>
          <span class="flex-none text-xs text-muted-foreground tabular-nums">{{
            $t('dashboard.scripts.runs.ms', { ms: run.result.durationMs })
          }}</span>
        </component>
      </li>
    </ul>
  </PanelCard>
</template>
