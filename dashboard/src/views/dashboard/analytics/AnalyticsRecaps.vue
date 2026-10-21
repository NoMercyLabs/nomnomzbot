<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Recaps tab: every kept stream recap (the end credits), newest first. Opening one shows it drawn by the real
// raid_recap widget; "Save as PNG" saves it at the size of the stream it was made for.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import ArrowDown from '~icons/nnz/arrow-down';
import ArrowsUpDown from '~icons/nnz/arrow-arrows-up-down';
import ArrowUp from '~icons/nnz/arrow-up';

import { RECAP_PAGE_SIZE, useStreamRecaps, withChannel, type StreamRecap, type StreamRecapPage } from '@/api/streamRecaps';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useTableSort } from '@/composables/useTableSort';
import { useTime } from '@/composables/useTime';

import RecapPreview from './RecapPreview.vue';
import { exportRecapPng, recapResolution } from './recapExport';

const { t } = useI18n();
const time = useTime();
const { channelId, current: channel } = useCurrentChannel();
const page = 'dashboard.analytics.recaps';
const DASH = '–';

// Every column sorts. The width classes are fixed so the table never reflows when a title or a number changes; the
// title takes what is left.
const COLUMNS = [
  {
    key: 'started',
    width: 'w-48',
    hidden: '',
  },
  {
    key: 'title',
    width: '',
    hidden: '',
  },
  {
    key: 'length',
    width: 'w-28',
    hidden: '',
  },
  {
    key: 'peak',
    width: 'w-24',
    hidden: '',
  },
  {
    key: 'chat',
    width: 'w-24',
    hidden: '',
  },
] as const;
const sort = useTableSort(['started', 'title', 'length', 'peak', 'chat'], {
  fallback: { column: 'started', direction: 'desc' },
  ascending: ['title'],
});

const current = ref(1);
const recaps = useStreamRecaps(channelId);
const kept = computed<StreamRecap[]>(() =>
  (recaps.data.value?.recaps ?? []).map((recap) => withChannel(recap, channel.value)));
const sortedRows = computed(() => sort.sorted(kept.value, sortValue));
const rows = computed(() =>
  sortedRows.value.slice((current.value - 1) * RECAP_PAGE_SIZE, current.value * RECAP_PAGE_SIZE));
const state = usePanelState(recaps, (data: StreamRecapPage) =>
  data.recaps.length === 0);
const total = computed(() => recaps.data.value?.total ?? 0);
const pages = computed(() => Math.max(1, Math.ceil(total.value / RECAP_PAGE_SIZE)));

watch([sort.column, sort.direction], () => {
  current.value = 1;
});

function sortValue(recap: StreamRecap, column: (typeof COLUMNS)[number]['key']): number | string {
  const values = {
    started: Date.parse(recap.fromAt ?? ''),
    title: recap.stats.stream.title ?? '',
    length: Number(recap.stats.duration ?? 0),
    peak: Number(recap.stats.peakViewers ?? -1),
    chat: Number(recap.stats.chat?.messages ?? 0),
  } as const satisfies Record<(typeof COLUMNS)[number]['key'], number | string>;

  return values[column];
}

function ariaSort(column: (typeof COLUMNS)[number]['key']): 'ascending' | 'descending' | 'none' {
  if (sort.column.value !== column)
    return 'none';

  return sort.direction.value === 'asc'
    ? 'ascending'
    : 'descending';
}

const opened = ref<StreamRecap | null>(null);
const preview = ref<InstanceType<typeof RecapPreview> | null>(null);
const saving = ref(false);
const failed = ref(false);

const resolution = computed(() => recapResolution(opened.value ?? {}));

function started(recap: StreamRecap): string {
  return time.weekdayDate(recap.fromAt);
}

function length(recap: StreamRecap): string {
  return time.duration(Number(recap.stats.duration ?? 0));
}

// A stream without viewer samples has no known peak: a dash, never 0.
function peak(recap: StreamRecap): string {
  return recap.stats.peakViewers === null || recap.stats.peakViewers === undefined
    ? DASH
    : String(recap.stats.peakViewers);
}

function title(recap: StreamRecap): string {
  return recap.stats.stream.title || DASH;
}

function chat(recap: StreamRecap): string {
  return String(recap.stats.chat?.messages ?? 0);
}

function close(open: boolean): void {
  if (!open) {
    opened.value = null;
  }
}

async function save(): Promise<void> {
  const frame = preview.value?.frame;

  if (!frame || !opened.value) return;

  saving.value = true;
  failed.value = false;

  try {
    const url = await exportRecapPng(frame, resolution.value);
    const link = document.createElement('a');

    link.href = url;
    link.download = `recap-${opened.value.stats.stream.startedAt.slice(0, 10)}.png`;
    link.click();
  } catch {
    failed.value = true;
  } finally {
    saving.value = false;
  }
}
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col p-4 lg:p-6">
    <PanelCard
      data-analytics-recaps
      :title="t(`${page}.title`)"
      :description="t(`${page}.count`, { count: recaps.data.value?.total ?? 0 }, recaps.data.value?.total ?? 0)"
      :state="state"
      :empty-text="t(`${page}.empty`)"
      :error-text="t('dashboard.analytics.error')"
      @retry="recaps.refetch()"
    >
      <Table class="min-w-176 table-fixed">
        <TableHeader>
          <TableRow class="hover:bg-transparent">
            <TableHead class="w-16" />
            <TableHead
              v-for="column in COLUMNS"
              :key="column.key"
              :class="[column.width, column.hidden]"
              :aria-sort="ariaSort(column.key)"
            >
              <Button
                variant="ghost"
                size="sm"
                class="-ml-2.5 gap-1.5!"
                :data-sort="column.key"
                @click="sort.press(column.key)"
              >
                {{ t(`${page}.columns.${column.key}`) }}
                <ArrowUp v-if="sort.column.value === column.key && sort.direction.value === 'asc'" class="size-4" />
                <ArrowDown v-else-if="sort.column.value === column.key" class="size-4" />
                <ArrowsUpDown v-else class="size-4 text-muted-foreground" />
              </Button>
            </TableHead>
            <TableHead class="w-24" />
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="recap in rows" :key="recap.streamId" data-recap-row>
            <TableCell>
              <Avatar class="size-10 rounded-md">
                <AvatarImage :src="recap.stats.channel?.avatarUrl ?? ''" alt="" />
                <AvatarFallback class="rounded-md">{{ recap.stats.channel?.name?.slice(0, 1) }}</AvatarFallback>
              </Avatar>
            </TableCell>
            <TableCell class="truncate font-medium">{{ started(recap) }}</TableCell>
            <TableCell class="truncate" :title="recap.stats.stream.title ?? ''">{{ title(recap) }}</TableCell>
            <TableCell class="tabular-nums">{{ length(recap) }}</TableCell>
            <TableCell class="tabular-nums">{{ peak(recap) }}</TableCell>
            <TableCell class="tabular-nums">{{ chat(recap) }}</TableCell>
            <TableCell class="text-right">
              <Button variant="outline" size="sm" data-recap-open @click="opened = recap">{{ t(`${page}.open`) }}</Button>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
      <div v-if="pages > 1" class="flex items-center justify-end gap-2 px-2 pt-3">
        <Button variant="ghost" size="sm" :disabled="current <= 1" @click="current--">{{ t(`${page}.previous`) }}</Button>
        <span class="text-sm text-muted-foreground tabular-nums">{{ t(`${page}.page`, { current, pages }) }}</span>
        <Button variant="ghost" size="sm" :disabled="current >= pages" @click="current++">{{ t(`${page}.next`) }}</Button>
      </div>
    </PanelCard>

    <Dialog :open="opened !== null" @update:open="close">
      <DialogContent class="sm:max-w-4xl">
        <DialogHeader>
          <DialogTitle>{{ opened ? title(opened) : '' }}</DialogTitle>
          <DialogDescription>
            {{ t(`${page}.${resolution.assumed ? 'sizeAssumed' : 'size'}`, { width: resolution.width, height: resolution.height }) }}
          </DialogDescription>
        </DialogHeader>
        <RecapPreview v-if="opened" ref="preview" :recap="opened" :size="resolution" />
        <p v-if="failed" class="text-sm text-destructive" role="alert">{{ t(`${page}.failed`) }}</p>
        <DialogFooter>
          <Button :disabled="saving" data-recap-save @click="save">{{ t(`${page}.save`) }}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
