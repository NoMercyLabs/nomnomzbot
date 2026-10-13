<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The custom commands: a search box, a sort choice and one row per command. The open command is the `selected` name.
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'

import { useCommands, type CommandListItem } from '@/api/commands'
import FilterBar, { type FilterOption } from '@/components/layout/FilterBar.vue'
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue'
import SearchField from '@/components/layout/SearchField.vue'
import StatusBadge from '@/components/layout/StatusBadge.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import { Button } from '@/components/ui/button'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { usePanelState } from '@/composables/usePanelState'

defineProps<{ selected: string | null }>()
const emit = defineEmits<{ pick: [name: string]; create: [] }>()

const { t } = useI18n()
const { channelId } = useCurrentChannel()
const commands = useCommands(channelId)

const search: Ref<string> = ref('')
const sort: Ref<string> = ref('uses')
const sortOptions: ComputedRef<FilterOption[]> = computed<FilterOption[]>(() => [
  { value: 'uses', label: t('dashboard.commands.list.sortUses') },
  { value: 'name', label: t('dashboard.commands.list.sortName') },
])

const panel = usePanelState(commands, (data: CommandListItem[]): boolean => data.length === 0)
// The empty state holds a button, so the panel shows it as ready content.
const state: ComputedRef<PanelState> = computed<PanelState>(() =>
  panel.value === 'empty' ? 'ready' : panel.value,
)
const none: ComputedRef<boolean> = computed<boolean>(() => panel.value === 'empty')

function matches(row: CommandListItem, needle: string): boolean {
  return [row.name, ...row.aliases].some((word: string): boolean =>
    word.toLowerCase().includes(needle),
  )
}

const rows: ComputedRef<CommandListItem[]> = computed<CommandListItem[]>(() => {
  const needle: string = search.value.trim().replace(/^!+/, '').toLowerCase()
  const found: CommandListItem[] = (commands.data.value ?? []).filter(
    (row: CommandListItem): boolean => matches(row, needle),
  )
  return found.sort((left: CommandListItem, right: CommandListItem): number =>
    sort.value === 'name'
      ? left.name.localeCompare(right.name)
      : Number(right.useCount) - Number(left.useCount),
  )
})

// The row names what kind of command it is; the reply text itself lives in the editor (owner 2026-10-13).
function kindOf(row: CommandListItem): string {
  if (row.tier === 'pipeline') return t('dashboard.commands.list.kindPipeline')
  if (row.tier === 'code') return t('dashboard.commands.list.kindCode')
  if ((row.templateResponses?.length ?? 0) > 0) return t('dashboard.commands.list.kindRandom')
  return t('dashboard.commands.list.kindText')
}

function detailOf(row: CommandListItem): string {
  const uses: number = Number(row.useCount)
  return `${kindOf(row)} · ${t('dashboard.commands.list.uses', { count: uses }, uses)}`
}
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col gap-3">
    <FilterBar v-model="sort" :label="$t('dashboard.commands.list.sort')" :options="sortOptions">
      <SearchField v-model="search" :placeholder="$t('dashboard.commands.list.search')" />
    </FilterBar>
    <PanelCard
      :title="$t('dashboard.commands.list.title')"
      :state="state"
      :error-text="$t('dashboard.commands.list.error')"
      @retry="commands.refetch()"
    >
      <div
        v-if="none"
        data-slot="panel-empty"
        class="flex h-full flex-col items-center justify-center gap-3 p-4 text-center text-sm text-muted-foreground"
      >
        {{ $t('dashboard.commands.list.empty') }}
        <Button type="button" size="sm" @click="emit('create')">{{
          $t('dashboard.commands.list.makeFirst')
        }}</Button>
      </div>
      <p v-else-if="rows.length === 0" class="p-4 text-center text-sm text-muted-foreground">
        {{ $t('dashboard.commands.list.noMatch') }}
      </p>
      <SummaryRow
        v-for="row in rows"
        v-else
        :key="row.id"
        pick
        :selected="row.name === selected"
        :title="`!${row.name}`"
        :detail="detailOf(row)"
        data-command-row
        :data-command-name="row.name"
        @click="emit('pick', row.name)"
      >
        <template v-if="!row.isEnabled" #end>
          <StatusBadge tone="off" :label="$t('dashboard.commands.list.off')" />
        </template>
      </SummaryRow>
    </PanelCard>
  </div>
</template>
