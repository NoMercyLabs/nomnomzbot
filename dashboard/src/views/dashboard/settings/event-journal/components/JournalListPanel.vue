<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The journal itself: what happened in the channel, newest first. The bot pages the list; a filter always goes
// back to page 1. The filters are view preferences, so they are remembered on this device.
import { refDebounced, useLocalStorage } from '@vueuse/core';
import { computed, ref, watch } from 'vue';
import ChevronLeft from '~icons/nnz/chevron-left';
import ChevronRight from '~icons/nnz/chevron-right';

import { useJournalPage, type JournalFilters } from '@/api/eventJournal';
import DateTime from '@/components/layout/DateTime.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

const PAGE_SIZE = 25;
const TYPE_PAUSE_MS = 300;

const { channelId } = useCurrentChannel();

const eventType = useLocalStorage('nnz.event-journal.eventType', '');
const source = useLocalStorage('nnz.event-journal.source', '');
const from = useLocalStorage('nnz.event-journal.from', '');
const to = useLocalStorage('nnz.event-journal.to', '');
const page = ref(1);

const typedType = refDebounced(
  computed(() => eventType.value),
  TYPE_PAUSE_MS,
);
const typedSource = refDebounced(
  computed(() => source.value),
  TYPE_PAUSE_MS,
);

watch([typedType, typedSource, from, to], () => {
  page.value = 1;
});

const filters = computed<JournalFilters>(() => ({
  eventType: typedType.value.trim(),
  source: typedSource.value.trim(),
  from: from.value,
  to: to.value,
  page: page.value,
  take: PAGE_SIZE,
}));

const journal = useJournalPage(channelId, filters);
const panelState = usePanelState(journal, (data) =>
  data.items.length === 0);

const rows = computed(() => journal.data.value?.items ?? []);
const total = computed(() => Number(journal.data.value?.totalCount ?? 0));
const head = computed(() => journal.data.value?.headPosition ?? 0);
const pages = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)));

function clear(): void {
  eventType.value = '';
  source.value = '';
  from.value = '';
  to.value = '';
}
</script>

<template>
  <PanelCard
    data-panel="journal-list"
    tier="lead"
    :title="$t('dashboard.settings.eventJournal.list.title')"
    :description="$t('dashboard.settings.eventJournal.list.description', { count: total, head })"
    :state="panelState"
    :empty-text="$t('dashboard.settings.eventJournal.list.empty')"
    :error-text="$t('dashboard.settings.eventJournal.list.error')"
    @retry="journal.refetch()"
  >
    <template #actions>
      <Button variant="ghost" size="sm" data-clear-filters @click="clear">
        {{ $t('dashboard.settings.eventJournal.list.clear') }}
      </Button>
    </template>
    <div class="flex flex-wrap items-center gap-2 px-3 py-3">
      <Input
        v-model="eventType"
        data-filter-type
        class="w-48"
        :aria-label="$t('dashboard.settings.eventJournal.list.eventType')"
        :placeholder="$t('dashboard.settings.eventJournal.list.eventType')"
      />
      <Input
        v-model="source"
        data-filter-source
        class="w-40"
        :aria-label="$t('dashboard.settings.eventJournal.list.source')"
        :placeholder="$t('dashboard.settings.eventJournal.list.source')"
      />
      <Input
        v-model="from"
        type="date"
        data-filter-from
        class="w-40"
        :aria-label="$t('dashboard.settings.eventJournal.list.from')"
      />
      <Input
        v-model="to"
        type="date"
        data-filter-to
        class="w-40"
        :aria-label="$t('dashboard.settings.eventJournal.list.to')"
      />
    </div>
    <Table>
      <TableHeader>
        <TableRow class="hover:bg-transparent">
          <TableHead>{{ $t('dashboard.settings.eventJournal.list.columns.position') }}</TableHead>
          <TableHead>{{ $t('dashboard.settings.eventJournal.list.columns.eventType') }}</TableHead>
          <TableHead>{{ $t('dashboard.settings.eventJournal.list.columns.source') }}</TableHead>
          <TableHead>{{ $t('dashboard.settings.eventJournal.list.columns.occurredAt') }}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-for="row in rows" :key="row.eventId" data-journal-row>
          <TableCell class="tabular-nums text-muted-foreground">{{ row.streamPosition }}</TableCell>
          <TableCell class="font-medium">{{ row.eventType }}</TableCell>
          <TableCell>{{ row.source }}</TableCell>
          <TableCell class="text-muted-foreground"><DateTime :value="row.occurredAt" variant="dateTimeFull" /></TableCell>
        </TableRow>
      </TableBody>
    </Table>
    <template #footer>
      <span class="text-sm text-muted-foreground tabular-nums" role="status">
        {{ $t('dashboard.settings.eventJournal.list.page', { page, pages }) }}
      </span>
      <div class="ml-auto flex items-center gap-2">
        <Button variant="outline"
                size="sm"
                data-prev
                :disabled="page <= 1"
                @click="page -= 1"
        >
          <ChevronLeft aria-hidden="true" />
          {{ $t('dashboard.settings.eventJournal.list.previous') }}
        </Button>
        <Button variant="outline"
                size="sm"
                data-next
                :disabled="page >= pages"
                @click="page += 1"
        >
          {{ $t('dashboard.settings.eventJournal.list.next') }}
          <ChevronRight aria-hidden="true" />
        </Button>
      </div>
    </template>
  </PanelCard>
</template>
