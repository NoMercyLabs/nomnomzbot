<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One person's real state: who they are, and their standing in every channel, each row named for its channel.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useSupportPerson } from '@/api/adminSupport';
import KeyValueList, { type KeyValueItem } from '@/components/layout/KeyValueList.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { usePanelState } from '@/composables/usePanelState';
import { useTime } from '@/composables/useTime';
import { merge } from '@/lib/array';

const props = defineProps<{ personId: string; justification: string }>();

const { t } = useI18n();

const query = useSupportPerson(
  () => props.personId,
  () => props.justification,
);
const person = computed(() => query.data.value ?? null);
const state = usePanelState(query, (data) =>
  data === null);

const time = useTime();

const facts = computed<KeyValueItem[]>(() => [
  { label: t('dashboard.admin.support.facts.username'), value: person.value?.username },
  { label: t('dashboard.admin.support.facts.platform'), value: person.value?.platform },
  { label: t('dashboard.admin.support.facts.twitchId'), value: person.value?.twitchUserId },
  {
    label: t('dashboard.admin.support.facts.lastSeen'),
    value: person.value?.lastSeenAt
      ? time.date(person.value.lastSeenAt)
      : null,
  },
]);

type Standing = {
  key: string;
  channel: string;
  kind: string;
  value: string;
};

const standings = computed<Standing[]>(() => {
  const view = person.value;

  if (!view)
    return [];

  const community = view.communityStandings.map((row): Standing => ({
    key: `c-${row.broadcasterId}`,
    channel: row.channelName,
    kind: t('dashboard.admin.support.standing.community'),
    value: row.standing,
  }));
  const moderation = view.moderationStandings.map((row): Standing => ({
    key: `m-${row.broadcasterId}-${row.provider}`,
    channel: row.channelName,
    kind: t('dashboard.admin.support.standing.moderation'),
    value: row.reason
      ? `${row.standing}: ${row.reason}`
      : row.standing,
  }));
  const trust = view.trustScores.map((row): Standing => ({
    key: `t-${row.broadcasterId}`,
    channel: row.channelName,
    kind: t('dashboard.admin.support.standing.trust'),
    value: String(row.trustScore),
  }));

  return Array.from(merge(community, moderation, trust));
});
</script>

<template>
  <PanelCard
    :title="t('dashboard.admin.support.facts.title')"
    pad
    :state="state"
    :error-text="t('dashboard.admin.support.error')"
    @retry="query.refetch()"
  >
    <div v-if="person" class="grid gap-4">
      <KeyValueList :items="facts" />
      <ul class="grid divide-y divide-border text-sm">
        <li
          v-for="row in standings"
          :key="row.key"
          data-standing-row
          class="flex items-center gap-3 py-2"
        >
          <span class="min-w-0 flex-1 font-medium">{{ row.channel }}</span>
          <span class="text-muted-foreground">{{ row.kind }}</span>
          <span>{{ row.value }}</span>
        </li>
      </ul>
    </div>
  </PanelCard>
</template>
