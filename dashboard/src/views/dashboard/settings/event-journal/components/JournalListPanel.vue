<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// What happened in the channel, newest first: what, who and when. The bot pages the list; a filter always goes
// back to page 1. The filters are view preferences, so they are remembered on this device.
import { useLocalStorage } from '@vueuse/core';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import ChevronLeft from '~icons/nnz/chevron-left';
import ChevronRight from '~icons/nnz/chevron-right';

import { useJournalPage, type JournalEntry, type JournalFilters } from '@/api/eventJournal';
import DateTime from '@/components/layout/DateTime.vue';
import FilterBar from '@/components/layout/FilterBar.vue';
import type { FilterOption } from '@/components/layout/filterOption';
import PanelCard from '@/components/layout/PanelCard.vue';
import UserName from '@/components/layout/UserName.vue';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Switch } from '@/components/ui/switch';
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
import { merge } from '@/lib/array';

import {
  EVENT_KINDS,
  EVENT_SOURCES,
  isEventKind,
  isEventSource,
  isHousekeeping,
  spokenEventName,
} from './journalWords';
import { foldRuns } from './journalRuns';
import { useJournalActors } from './useJournalActors';

const PAGE_SIZE = 25;
const HIDING_PAGE_SIZE = 100;
const ALL = 'all';

const { t, te } = useI18n();
const { channelId } = useCurrentChannel();

const eventType = useLocalStorage('nnz.event-journal.eventType', '');
const source = useLocalStorage('nnz.event-journal.source', '');
const from = useLocalStorage('nnz.event-journal.from', '');
const to = useLocalStorage('nnz.event-journal.to', '');
const showHousekeeping = useLocalStorage('nnz.event-journal.housekeeping', false);
const page = ref(1);

// A remembered value that is not on the list (an old free-text filter) reads as "all".
const kindChoice = computed({
  get: () =>
    isEventKind(eventType.value)
      ? eventType.value
      : ALL,
  set: (value: string) => {
    eventType.value = value === ALL
      ? ''
      : value;
  },
});
const sourceChoice = computed({
  get: () =>
    isEventSource(source.value)
      ? source.value
      : ALL,
  set: (value: string) => {
    source.value = value === ALL
      ? ''
      : value;
  },
});

const kindOptions = computed(() => merge(
  [{ value: ALL, label: t('dashboard.settings.eventJournal.list.allKinds') }],
  EVENT_KINDS.map((kind) => ({ value: kind, label: t(`dashboard.settings.eventJournal.events.${kind}`) }))
    .sort((a, b) => a.label.localeCompare(b.label)),
) satisfies readonly FilterOption[]);
const sourceOptions = computed(() => merge(
  [{ value: ALL, label: t('dashboard.settings.eventJournal.list.allSources') }],
  EVENT_SOURCES.map((known) => ({ value: known, label: t(`dashboard.settings.eventJournal.sources.${known}`) })),
) satisfies readonly FilterOption[]);

// Hidden housekeeping thins a page out, so the bot reads a bigger page to keep the list full.
const take = computed(() =>
  showHousekeeping.value
    ? PAGE_SIZE
    : HIDING_PAGE_SIZE);

watch([kindChoice, sourceChoice, from, to, showHousekeeping], () => {
  page.value = 1;
});

const filters = computed(() => ({
  eventType: kindChoice.value === ALL
    ? ''
    : kindChoice.value,
  source: sourceChoice.value === ALL
    ? ''
    : sourceChoice.value,
  from: from.value,
  to: to.value,
  page: page.value,
  take: take.value,
}) as const satisfies JournalFilters);

const journal = useJournalPage(channelId, filters);
// A page of only bot activity reads as empty while that activity is hidden.
const panelState = usePanelState(journal, (data) =>
  !data.items.some((row) => showHousekeeping.value || !isHousekeeping(row.eventType)));

const fetched = computed(() => journal.data.value?.items ?? []);
const rows = computed(() =>
  showHousekeeping.value
    ? fetched.value
    : fetched.value.filter((row) => !isHousekeeping(row.eventType)));
const hiddenCount = computed(() => fetched.value.length - rows.value.length);
const total = computed(() => Number(journal.data.value?.totalCount ?? 0));
const pages = computed(() => Math.max(1, Math.ceil(total.value / take.value)));
const actors = useJournalActors(channelId, rows);
const runs = computed(() => foldRuns(rows.value));
const filtered = computed(() =>
  kindChoice.value !== ALL || sourceChoice.value !== ALL || from.value !== '' || to.value !== '');

function what(row: JournalEntry): string {
  // The server spells one type name in British English; the translation key is American.
  // cspell:disable-next-line -- the server's type name
  const key = `dashboard.settings.eventJournal.events.${row.eventType.replace('Cancelled', 'Canceled')}`;

  if (te(key, 'en')) {
    return t(key);
  }

  // A dotted name is Twitch's own notice as it arrived (channel.chat.message), kept apart from the bot's reading of it.
  if (row.eventType.includes('.')) {
    const spoken = spokenEventName(row.eventType.replace(/^channel\./, ''));
    const words = spoken.charAt(0).toLowerCase() + spoken.slice(1);

    return t('dashboard.settings.eventJournal.twitchNotice', { what: words });
  }

  return spokenEventName(row.eventType);
}

// A server clock a little ahead of the browser must not read as "in 2 seconds".
function shownAt(row: JournalEntry): string {
  const at = Date.parse(row.occurredAt);
  const now = Date.now();

  return new Date(at > now
    ? now
    : at).toISOString();
}

function actorOf(row: JournalEntry) {
  return row.actorUserId
    ? actors.value.get(row.actorUserId)
    : undefined;
}

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
    :description="$t('dashboard.settings.eventJournal.list.description', { count: $n(total) })"
    :state="panelState"
    :empty-text="$t('dashboard.settings.eventJournal.list.empty')"
    :error-text="$t('dashboard.settings.eventJournal.list.error')"
    @retry="journal.refetch()"
  >
    <template v-if="filtered" #actions>
      <Button variant="outline" size="sm" data-clear-filters @click="clear">
        {{ $t('dashboard.settings.eventJournal.list.clear') }}
      </Button>
    </template>
    <div class="flex flex-wrap items-center gap-2 px-3 py-3">
      <FilterBar
        v-model="kindChoice"
        data-filter-type
        :label="$t('dashboard.settings.eventJournal.list.kind')"
        :options="kindOptions"
      />
      <FilterBar
        v-model="sourceChoice"
        data-filter-source
        :label="$t('dashboard.settings.eventJournal.list.source')"
        :options="sourceOptions"
      />
      <Label class="font-normal">
        <span class="text-muted-foreground">{{ $t('dashboard.settings.eventJournal.list.from') }}</span>
        <Input v-model="from" type="date" data-filter-from class="w-40" />
      </Label>
      <Label class="font-normal">
        <span class="text-muted-foreground">{{ $t('dashboard.settings.eventJournal.list.to') }}</span>
        <Input v-model="to" type="date" data-filter-to class="w-40" />
      </Label>
      <div class="flex items-center gap-2">
        <Switch id="journal-housekeeping" v-model="showHousekeeping" data-show-housekeeping />
        <Label for="journal-housekeeping" class="font-normal">{{ $t('dashboard.settings.eventJournal.list.housekeeping') }}</Label>
      </div>
    </div>
    <Table>
      <TableHeader>
        <TableRow class="hover:bg-transparent">
          <TableHead>{{ $t('dashboard.settings.eventJournal.list.columns.what') }}</TableHead>
          <TableHead class="text-right">{{ $t('dashboard.settings.eventJournal.list.columns.when') }}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <TableRow v-for="{ entry: row, count } in runs" :key="row.eventId" data-journal-row>
          <TableCell class="font-medium">
            <!-- The person, when the bot saved one, is the subject of the line. -->
            <UserName
              v-if="actorOf(row)"
              class="mr-2"
              :user-id="actorOf(row)?.twitchUserId"
              :name="actorOf(row)?.name ?? ''"
              :color="actorOf(row)?.color"
            />
            <!-- No person on the row: an API key or the bot itself made the change. Old rows record none. -->
            <Badge
              v-else-if="row.actorKind === 'api_key' || row.actorKind === 'system'"
              variant="secondary"
              class="mr-2"
              data-actor-kind
            >
              {{ $t(`dashboard.settings.eventJournal.list.actor.${row.actorKind}`) }}
            </Badge>
            <span data-what>{{ what(row) }}</span>
            <span v-if="count > 1" data-repeat class="ml-2 font-normal text-muted-foreground tabular-nums">{{ $t('dashboard.settings.eventJournal.list.repeat', { count: $n(count) }) }}</span>
          </TableCell>
          <TableCell class="text-right text-muted-foreground">
            <!-- A reused row keeps its first "now"; a fresh key gives each new moment a fresh clock. -->
            <DateTime :key="shownAt(row)" :value="shownAt(row)" variant="relative" />
          </TableCell>
        </TableRow>
      </TableBody>
    </Table>
    <template #footer>
      <span class="text-sm text-muted-foreground tabular-nums" role="status">
        {{ $t('dashboard.settings.eventJournal.list.page', { page: $n(page), pages: $n(pages) }) }}
      </span>
      <span v-if="hiddenCount > 0" data-hidden-count class="text-sm text-muted-foreground tabular-nums">
        {{ $t('dashboard.settings.eventJournal.list.hidden', { count: $n(hiddenCount) }) }}
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
