<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The dashboard tour under the hero: five tabs, each showing a sample of one dashboard page.
import { useI18n } from 'vue-i18n'

import PanelCard from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'

import DemoRows from './DemoRows.vue'

type Tab = {
  id: 'dashboard' | 'chat' | 'mod' | 'rules' | 'alerts'
  group: 'home' | 'chat' | 'moderation' | 'stream'
}

const tabs: Tab[] = [
  { id: 'dashboard', group: 'home' },
  { id: 'chat', group: 'chat' },
  { id: 'mod', group: 'moderation' },
  { id: 'rules', group: 'moderation' },
  { id: 'alerts', group: 'stream' },
]
const stats: string[] = ['viewers', 'followers', 'chatters', 'uptime']
const filters: {
  name: string
  detail?: string
  on: boolean
}[] = [
  { name: 'linkFilter', on: true },
  { name: 'capsFilter', detail: 'capsDetail', on: true },
  { name: 'bannedPhrases', on: false },
]
const blocked: string[] = ['blocked1', 'blocked2', 'blocked3']

const { t } = useI18n()
</script>

<template>
  <PanelCard
    :title="t('site.home.demo.channel')"
    :description="t('site.home.demo.role')"
    pad
    class="w-full text-left"
  >
    <Tabs default-value="dashboard" class="gap-3">
      <!-- A narrow screen scrolls the row sideways; wrapping drops one tab onto a line of its own. -->
      <TabsList
        :aria-label="t('site.home.demo.label')"
        class="w-full justify-start overflow-x-auto"
      >
        <TabsTrigger v-for="tab in tabs" :key="tab.id" :value="tab.id">{{
          t(`site.home.demo.tabs.${tab.id}`)
        }}</TabsTrigger>
      </TabsList>

      <TabsContent v-for="tab in tabs" :key="tab.id" :value="tab.id" class="flex flex-col gap-2">
        <p class="text-xs font-medium text-muted-foreground">
          {{ t(`site.home.demo.groups.${tab.group}`) }}
        </p>
        <h3 class="text-sm font-semibold">{{ t(`site.home.demo.${tab.id}.title`) }}</h3>

        <div
          v-if="tab.id === 'dashboard'"
          class="grid grid-cols-2 gap-1 rounded-[12px] border border-border bg-muted p-1 sm:grid-cols-4"
        >
          <div v-for="stat in stats" :key="stat" class="rounded-[8px] px-3 py-2">
            <div class="text-lg font-semibold">
              {{ t(`site.home.demo.dashboard.${stat}Value`) }}
            </div>
            <div class="text-sm text-muted-foreground">
              {{ t(`site.home.demo.dashboard.${stat}`) }}
            </div>
          </div>
        </div>
        <DemoRows v-else-if="tab.id === 'chat'" kind="requests" />
        <DemoRows v-else-if="tab.id === 'mod'" kind="queue" />
        <DemoRows v-else-if="tab.id === 'alerts'" kind="alerts" />
        <template v-else>
          <RowGroup>
            <SummaryRow
              v-for="filter in filters"
              :key="filter.name"
              :title="t(`site.home.demo.rules.${filter.name}`)"
              :detail="filter.detail ? t(`site.home.demo.rules.${filter.detail}`) : undefined"
            >
              <template #end>
                <span class="flex items-center gap-2 text-xs font-medium text-muted-foreground">
                  <span
                    class="size-2 rounded-full"
                    :class="filter.on ? 'bg-success' : 'bg-muted-foreground'"
                    aria-hidden="true"
                  />
                  {{ t(filter.on ? 'site.home.demo.rules.on' : 'site.home.demo.rules.off') }}
                </span>
              </template>
            </SummaryRow>
          </RowGroup>
          <p class="text-xs font-medium text-muted-foreground">
            {{ t('site.home.demo.rules.blockedTitle') }}
          </p>
          <ul class="flex flex-wrap gap-1">
            <li
              v-for="term in blocked"
              :key="term"
              class="rounded-[8px] border border-border bg-muted px-2 py-1 text-sm"
            >
              {{ t(`site.home.demo.rules.${term}`) }}
            </li>
          </ul>
        </template>
      </TabsContent>
    </Tabs>
  </PanelCard>
</template>
