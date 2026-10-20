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
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { RECAP_PAGE_SIZE, useStreamRecaps, withChannel, type StreamRecap, type StreamRecapPage } from '@/api/streamRecaps';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar';
import { Button } from '@/components/ui/button';
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog';
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useTime } from '@/composables/useTime';

import RecapPreview from './RecapPreview.vue';
import { exportRecapPng, recapResolution } from './recapExport';

const { t } = useI18n();
const time = useTime();
const { channelId, current: channel } = useCurrentChannel();
const page = 'dashboard.analytics.recaps';

const current = ref(1);
const recaps = useStreamRecaps(channelId, current);
const rows = computed<StreamRecap[]>(() =>
  (recaps.data.value?.recaps ?? []).map((kept) => withChannel(kept, channel.value)));
const state = usePanelState(recaps, (data: StreamRecapPage) =>
  data.recaps.length === 0);
const total = computed(() => recaps.data.value?.total ?? 0);
const pages = computed(() => Math.max(1, Math.ceil(total.value / RECAP_PAGE_SIZE)));

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

function sizeLabel(recap: StreamRecap): string {
  const size = recapResolution(recap);

  return t(`${page}.${size.assumed
    ? 'sizeAssumed'
    : 'size'}`, { width: size.width, height: size.height });
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
      <Table>
        <TableHeader>
          <TableRow class="hover:bg-transparent">
            <TableHead class="w-16" />
            <TableHead>{{ t(`${page}.columns.started`) }}</TableHead>
            <TableHead>{{ t(`${page}.columns.title`) }}</TableHead>
            <TableHead>{{ t(`${page}.columns.length`) }}</TableHead>
            <TableHead class="max-md:hidden">{{ t(`${page}.columns.size`) }}</TableHead>
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
            <TableCell class="font-medium">{{ started(recap) }}</TableCell>
            <TableCell>{{ recap.stats.stream.title }}</TableCell>
            <TableCell class="tabular-nums">{{ length(recap) }}</TableCell>
            <TableCell class="text-muted-foreground max-md:hidden">{{ sizeLabel(recap) }}</TableCell>
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
          <DialogTitle>{{ opened?.stats.stream.title }}</DialogTitle>
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
