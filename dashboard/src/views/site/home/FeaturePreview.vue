<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One small preview of a feature group, built from the dashboard's own components (stat tiles, summary rows,
// status badges, bar lists) and filled from the sample file. Nothing here is read from a real channel.
import { computed, type Component } from 'vue';
import { useI18n } from 'vue-i18n';
import ChatBubble from '~icons/nnz/chat-bubble';
import Layout from '~icons/nnz/layout';
import SplitBranch from '~icons/nnz/split-branch';
import Volume from '~icons/nnz/volume-two';

import BarList, { type BarItem } from '@/components/layout/BarList.vue';
import PlatformLogo from '@/components/layout/PlatformLogo.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatStrip from '@/components/layout/StatStrip.vue';
import StatTile from '@/components/layout/StatTile.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { lookup } from '@/lib/array';

import { sampleChannel, sampleRows, type FeatureGroup, type SampleRow } from './sampleChannel';

const props = defineProps<{ group: FeatureGroup }>();

const stepIcons = {
  trigger: ChatBubble,
  check: SplitBranch,
  chat: ChatBubble,
  overlay: Layout,
} as const satisfies Record<string, Component>;

const { t } = useI18n();
const rows = computed<readonly SampleRow[]>(() => sampleRows[props.group]);
const bars = computed<BarItem[]>(() =>
  rows.value.map((row) => ({
    key: row.id,
    label: row.who ?? row.id,
    detail: t(`site.home.features.${props.group}.unit`),
    value: row.value ?? 0,
  })));

function words(row: SampleRow, part: 'title' | 'detail' | 'status'): string {
  return t(`site.home.features.${props.group}.rows.${row.id}.${part}`, {
    who: row.who ?? '',
    channel: sampleChannel.handle,
    count: sampleChannel.raiders,
  });
}

function stepIcon(row: SampleRow): Component {
  return lookup(stepIcons, row.id) ?? Volume;
}
</script>

<template>
  <BarList v-if="group === 'loyalty'" :items="bars" class="px-2 py-1" />
  <StatStrip v-else-if="group === 'analytics'">
    <StatTile
      v-for="row in rows"
      :key="row.id"
      :label="words(row, 'title')"
      :value="row.value ?? 0"
      :context="words(row, 'detail')"
    />
  </StatStrip>
  <RowGroup v-else>
    <SummaryRow
      v-for="(row, index) in rows"
      :key="row.id"
      :title="words(row, 'title')"
      :detail="words(row, 'detail')"
    >
      <template v-if="group === 'pipelines'" #leading>
        <span class="grid size-8 flex-none place-items-center rounded-full bg-muted" aria-hidden="true">
          <component :is="stepIcon(row)" class="size-4" />
        </span>
        <span class="text-xs text-muted-foreground tabular-nums">{{ index + 1 }}</span>
      </template>
      <template v-else-if="row.platform" #leading>
        <PlatformLogo :platform="row.platform" class="size-5 flex-none" />
      </template>
      <template v-if="row.tone" #end>
        <StatusBadge :tone="row.tone" :label="words(row, 'status')" class="text-xs" />
      </template>
    </SummaryRow>
  </RowGroup>
</template>
