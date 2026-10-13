<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Development only: every shared building block in each of its states (spec section 5, step 2).
import { ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'

import BoardCell from '@/components/layout/BoardCell.vue'
import BoardGrid from '@/components/layout/BoardGrid.vue'
import FilterBar, { type FilterOption } from '@/components/layout/FilterBar.vue'
import KeyValueList, { type KeyValueItem } from '@/components/layout/KeyValueList.vue'
import ListDetail from '@/components/layout/ListDetail.vue'
import PageHeader from '@/components/layout/PageHeader.vue'
import PageTabs, { type PageTab } from '@/components/layout/PageTabs.vue'
import PanelCard from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import SearchField from '@/components/layout/SearchField.vue'
import SettingRow from '@/components/layout/SettingRow.vue'
import StatStrip from '@/components/layout/StatStrip.vue'
import StatTile from '@/components/layout/StatTile.vue'
import StatusBadge from '@/components/layout/StatusBadge.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import { Button } from '@/components/ui/button'
import { usePageView } from '@/composables/usePageView'

const { t } = useI18n()

const tabs: PageTab[] = [
  { value: 'blocks', label: t('dashboard.catalogue.tabs.blocks') },
  { value: 'states', label: t('dashboard.catalogue.tabs.states') },
  { value: 'layouts', label: t('dashboard.catalogue.tabs.layouts') },
]
const view = usePageView(tabs.map((tab: PageTab) => tab.value))

const kvItems: KeyValueItem[] = [
  { label: t('dashboard.catalogue.kv.region'), value: t('dashboard.catalogue.kv.regionValue') },
  { label: t('dashboard.catalogue.kv.uptime'), value: null, reason: t('dashboard.catalogue.kv.uptimeReason') },
]

const stateOptions: FilterOption[] = ['all', 'on', 'off'].map((key: string) => ({
  value: key,
  label: t(`dashboard.catalogue.filter.${key}`),
}))
const sinceOptions: FilterOption[] = ['day', 'week', 'month', 'quarter', 'year'].map((key: string) => ({
  value: key,
  label: t(`dashboard.catalogue.filter.${key}`),
}))

const ttsOn: Ref<boolean> = ref(true)
const modsOn: Ref<boolean> = ref(false)
const stateFilter: Ref<string> = ref('all')
const sinceFilter: Ref<string> = ref('week')
const query: Ref<string> = ref('')
const retries: Ref<number> = ref(0)
const detailOpen: Ref<boolean> = ref(false)
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col">
    <PageHeader :title="t('dashboard.catalogue.title')" :crumb="t('dashboard.catalogue.crumb')">
      <template #status>
        <StatusBadge tone="live" :label="t('dashboard.catalogue.status.live')" />
      </template>
      <template #meta>{{ t('dashboard.catalogue.meta') }}</template>
      <template #actions>
        <Button variant="outline">{{ t('dashboard.catalogue.outline') }}</Button>
        <Button>{{ t('dashboard.catalogue.primary') }}</Button>
      </template>
      <template #tabs><PageTabs :tabs="tabs" /></template>
    </PageHeader>

    <div class="min-h-0 flex-1 overflow-y-auto">
      <div class="grid gap-4 px-6 pt-4 pb-6">
        <p class="text-sm text-muted-foreground">{{ t('dashboard.catalogue.view', { view }) }}</p>

        <template v-if="view === 'blocks'">
          <StatStrip>
            <StatTile
              :label="t('dashboard.catalogue.stats.messages')"
              :value="54431"
              :context="t('dashboard.catalogue.stats.messagesContext')"
            />
            <StatTile
              :label="t('dashboard.catalogue.stats.channels')"
              :value="26"
              :context="t('dashboard.catalogue.stats.channelsContext')"
            />
            <StatTile
              :label="t('dashboard.catalogue.stats.hidden')"
              :value="8019"
              :context="t('dashboard.catalogue.stats.hiddenContext')"
              warn
            />
          </StatStrip>

          <div class="flex flex-wrap gap-4">
            <StatusBadge tone="ok" :label="t('dashboard.catalogue.status.ok')" />
            <StatusBadge tone="warn" :label="t('dashboard.catalogue.status.warn')" />
            <StatusBadge tone="bad" :label="t('dashboard.catalogue.status.bad')" />
            <StatusBadge tone="live" :label="t('dashboard.catalogue.status.live')" />
            <StatusBadge tone="off" :label="t('dashboard.catalogue.status.off')" />
          </div>

          <FilterBar v-model="stateFilter" :label="t('dashboard.catalogue.filter.state')" :options="stateOptions">
            <SearchField v-model="query" :placeholder="t('dashboard.catalogue.search')" />
          </FilterBar>
          <FilterBar v-model="sinceFilter" :label="t('dashboard.catalogue.filter.since')" :options="sinceOptions" />

          <PanelCard :title="t('dashboard.catalogue.panel.ready')" pad>
            <KeyValueList :items="kvItems" />
          </PanelCard>

          <PanelCard :title="t('dashboard.catalogue.panel.ready')">
            <RowGroup>
              <SettingRow
                v-model="ttsOn"
                :title="t('dashboard.catalogue.settings.tts')"
                :description="t('dashboard.catalogue.settings.ttsHelp')"
              />
              <SettingRow
                v-model="modsOn"
                :title="t('dashboard.catalogue.settings.mods')"
                :description="t('dashboard.catalogue.settings.modsHelp')"
              />
            </RowGroup>
          </PanelCard>
        </template>

        <div v-else-if="view === 'states'" class="grid h-72 grid-cols-2 gap-4 xl:grid-cols-3">
          <PanelCard :title="t('dashboard.catalogue.panel.ready')" tier="lead">
            <RowGroup>
              <SummaryRow
                :title="t('dashboard.catalogue.panel.readyRow')"
                :detail="t('dashboard.catalogue.panel.readyDetail')"
              />
            </RowGroup>
          </PanelCard>
          <PanelCard :title="t('dashboard.catalogue.panel.loading')" state="loading" />
          <PanelCard
            :title="t('dashboard.catalogue.panel.empty')"
            state="empty"
            :empty-text="t('dashboard.catalogue.panel.emptyText')"
          />
          <PanelCard
            :title="t('dashboard.catalogue.panel.error')"
            state="error"
            :description="t('dashboard.catalogue.panel.retried', { count: retries })"
            @retry="retries += 1"
          />
          <PanelCard :title="t('dashboard.catalogue.panel.quiet')" tier="quiet" />
        </div>

        <template v-else>
          <div class="flex h-80 flex-col">
            <BoardGrid>
              <BoardCell :span="7" :rows="2">
                <PanelCard :title="t('dashboard.catalogue.layout.board')" tier="lead" />
              </BoardCell>
              <BoardCell :span="5">
                <PanelCard :title="t('dashboard.catalogue.layout.cell')" />
              </BoardCell>
              <BoardCell :span="5">
                <PanelCard :title="t('dashboard.catalogue.layout.cell')" tier="quiet" />
              </BoardCell>
            </BoardGrid>
          </div>
          <div class="flex h-80 flex-col">
            <ListDetail :show-detail="detailOpen">
              <template #list>
                <PanelCard :title="t('dashboard.catalogue.layout.list')">
                  <RowGroup>
                    <SummaryRow
                      pick
                      :title="t('dashboard.catalogue.panel.readyRow')"
                      @click="detailOpen = !detailOpen"
                    />
                  </RowGroup>
                </PanelCard>
              </template>
              <template #detail>
                <PanelCard :title="t('dashboard.catalogue.layout.detail')" tier="lead" />
              </template>
              <template #aside>
                <PanelCard :title="t('dashboard.catalogue.layout.aside')" tier="quiet" />
              </template>
            </ListDetail>
          </div>
        </template>
      </div>
    </div>
  </div>
</template>
