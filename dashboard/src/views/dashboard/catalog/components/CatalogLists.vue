<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The key-value list, the real-members list, the virtual list and the setting rows.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useCommunityFirstPage, type CommunityRow } from '@/api/community';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import type { KeyValueItem } from '@/components/layout/keyValueItem';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import UserName from '@/components/layout/UserName.vue';
import VirtualList from '@/components/layout/VirtualList.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import type { PanelState } from '@/composables/usePanelState';

const { t } = useI18n();

const kvItems: KeyValueItem[] = [
  { label: t('dashboard.catalog.kv.region'), value: t('dashboard.catalog.kv.regionValue') },
  {
    label: t('dashboard.catalog.kv.uptime'),
    value: null,
    reason: t('dashboard.catalog.kv.uptimeReason'),
  },
];

const ttsOn = ref(true);
const modsOn = ref(false);

// Real members of the open channel (first page of the community list), so the profile dialog opens on real data.
const { channelId } = useCurrentChannel();
const community = useCommunityFirstPage(channelId);
const people = computed<CommunityRow[]>(() => community.data.value ?? []);

const profileState = computed<PanelState>(() => {
  if (community.isError.value)
    return 'error';

  if (community.isPending.value)
    return 'loading';

  return people.value.length > 0
    ? 'ready'
    : 'empty';
});

// Demo data for the VirtualList example only: 5,000 generated rows, some long enough to wrap.
interface DemoRow {
  id: number;
  text: string;
}
const demoRows = ref<DemoRow[]>(
  Array.from({ length: 5000 }, (_: undefined, id: number) => ({
    id,
    text:
      t('dashboard.catalog.virtual.row', { number: id + 1 }) +
      (id % 7 === 0
        ? ' ' + t('dashboard.catalog.virtual.long')
        : ''),
  })),
);

function addDemoRow(): void {
  const id = demoRows.value.length;

  demoRows.value = [
    ...demoRows.value,
    { id, text: t('dashboard.catalog.virtual.row', { number: id + 1 }) },
  ];
}
</script>

<template>
  <section data-catalog="lists" class="grid gap-4">
    <PanelCard :title="t('dashboard.catalog.panel.ready')" pad>
      <KeyValueList :items="kvItems" />
    </PanelCard>

    <PanelCard
      :title="t('dashboard.catalog.profile.title')"
      :state="profileState"
      :empty-text="t('dashboard.catalog.profile.empty')"
      pad
      @retry="community.refetch()"
    >
      <p class="mb-3 text-sm text-muted-foreground">
        {{ t('dashboard.catalog.profile.hint') }}
      </p>
      <ul class="flex flex-wrap gap-x-4 gap-y-2 text-sm">
        <li v-for="person in people" :key="person.id">
          <UserName :user-id="person.id" :name="person.displayName" :color="person.color" />
        </li>
      </ul>
    </PanelCard>

    <PanelCard :title="t('dashboard.catalog.virtual.title')" pad>
      <div class="flex flex-col gap-3">
        <div class="flex items-center gap-4">
          <Button variant="outline" @click="addDemoRow">{{
            t('dashboard.catalog.virtual.add')
          }}</Button>
          <span class="text-sm text-muted-foreground">
            {{ t('dashboard.catalog.virtual.count', { count: demoRows.length }) }}
          </span>
        </div>
        <div class="flex h-72 flex-col">
          <VirtualList
            stick
            :items="demoRows"
            :item-key="(row: DemoRow) => row.id"
            :estimate-size="36"
            :label="t('dashboard.catalog.virtual.title')"
          >
            <template #default="{ item }">
              <p class="px-3 py-2 text-sm">{{ item.text }}</p>
            </template>
          </VirtualList>
        </div>
      </div>
    </PanelCard>

    <PanelCard :title="t('dashboard.catalog.panel.ready')">
      <RowGroup>
        <SettingRow
          v-model="ttsOn"
          :title="t('dashboard.catalog.settings.tts')"
          :description="t('dashboard.catalog.settings.ttsHelp')"
        />
        <SettingRow
          v-model="modsOn"
          :title="t('dashboard.catalog.settings.mods')"
          :description="t('dashboard.catalog.settings.modsHelp')"
        />
      </RowGroup>
    </PanelCard>
  </section>
</template>
