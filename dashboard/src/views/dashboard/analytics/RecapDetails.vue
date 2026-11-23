<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The recap as plain details, for a channel with no recap overlay installed: every section of the kept recap
// with its numbers and names. It lists what was stored and draws nothing the stream did not produce.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { StreamRecap } from '@/api/streamRecaps';
import { useTime } from '@/composables/useTime';
import { numberOrNull } from '@/lib/number';

const props = defineProps<{ recap: StreamRecap }>();

const { t, n } = useI18n();
const time = useTime();
const page = 'dashboard.analytics.recaps.details';
const DASH = '–';

type Entry = { name: string; value: string };
type Group = { heading: string; entries: Entry[] };
type Section = {
  key: string;
  total: string;
  groups: Group[];
};

type Counted = { name: string; count?: number | string };
type Ranked = { total?: number | string | null; top?: Counted[] | null };

function count(value: number | string | null | undefined): string {
  const known = numberOrNull(value);

  return known === null
    ? DASH
    : n(known);
}

function counted(items: Counted[] | null | undefined): Entry[] {
  return (items ?? []).map((item) => ({ name: item.name, value: count(item.count) }));
}

function names(items: string[] | null | undefined): Entry[] {
  return (items ?? []).map((name) => ({ name, value: '' }));
}

function group(heading: string, entries: Entry[]) {
  return { heading, entries } as const satisfies Group;
}

function ranked(key: string, list: Ranked | null | undefined): Section {
  return {
    key,
    total: count(list?.total),
    groups: [group('', counted(list?.top))],
  };
}

const sections = computed<Section[]>(() => {
  const stats = props.recap.stats;
  const raids = stats.raidsIn ?? [];
  const raidEntries = raids.map((raid) => ({ name: raid.name, value: count(raid.viewers) }));
  const subGroups = [
    group(t(`${page}.gifted`), counted(stats.gifters)),
    group(t(`${page}.subscribed`), names(stats.selfSubs)),
  ];

  return [
    {
      key: 'stream',
      total: time.duration(Number(stats.duration ?? 0)),
      groups: [
        group('', [
          { name: t(`${page}.peak`), value: count(stats.peakViewers) },
          { name: t(`${page}.messages`), value: count(stats.chat?.messages) },
          { name: t(`${page}.chatters`), value: count(stats.chat?.uniqueChatters) },
        ]),
      ],
    },
    {
      key: 'subs',
      total: count(stats.subs?.total),
      groups: subGroups,
    },
    {
      key: 'followers',
      total: count(stats.followers),
      groups: [group('', names(stats.newFollowers))],
    },
    {
      key: 'raids',
      total: count(raids.length),
      groups: [group('', raidEntries)],
    },
    {
      key: 'chatters',
      total: count(stats.chat?.uniqueChatters),
      groups: [group('', counted(stats.topChatters))],
    },
    ranked('songs', stats.songRequesters),
    ranked('bits', stats.bits),
    ranked('redemptions', stats.redemptions),
    {
      key: 'commands',
      total: count(stats.commandsRun?.total),
      groups: [group('', counted(stats.commandsRun?.top))],
    },
  ];
});

function isEmpty(section: Section): boolean {
  return section.groups.every((one) => one.entries.length === 0);
}
</script>

<template>
  <div data-recap-details class="grid max-h-[60vh] gap-3 overflow-y-auto sm:grid-cols-2">
    <section
      v-for="section in sections"
      :key="section.key"
      :data-recap-section="section.key"
      class="flex flex-col gap-2 rounded-lg border p-4"
    >
      <div class="flex items-baseline justify-between gap-3">
        <h3 class="text-sm font-medium">{{ t(`${page}.sections.${section.key}`) }}</h3>
        <span class="text-lg font-semibold tabular-nums">{{ section.total }}</span>
      </div>
      <p v-if="isEmpty(section)" class="text-sm text-muted-foreground">
        {{ t(`${page}.none`) }}
      </p>
      <template v-for="one in section.groups" :key="one.heading">
        <template v-if="one.entries.length">
          <p v-if="one.heading" class="pt-1 text-xs font-medium text-muted-foreground">{{ one.heading }}</p>
          <ul class="flex flex-col gap-1 text-sm">
            <li
              v-for="(entry, index) in one.entries"
              :key="index"
              data-recap-entry
              class="flex items-baseline justify-between gap-3"
            >
              <span class="truncate">{{ entry.name }}</span>
              <span class="text-muted-foreground tabular-nums">{{ entry.value }}</span>
            </li>
          </ul>
        </template>
      </template>
    </section>
  </div>
</template>
