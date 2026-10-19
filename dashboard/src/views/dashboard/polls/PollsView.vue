<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->

<script setup lang="ts">
// One lead panel for what runs now (or the Start a poll action), then the history of Twitch polls,
// predictions and the bot's own chat polls as quiet cards. Polls nobody voted on fold into one line.
import { useLocalStorage } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  isPollRunning,
  isPredictionRunning,
  usePollHistory,
  usePolls,
  usePredictions,
  type Poll,
  type PollHistoryItem,
  type Prediction,
} from '@/api/liveOps';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useCan } from '@/composables/useCan';
import { usePanelState } from '@/composables/usePanelState';
import PollDialog from '@/views/dashboard/home/components/PollDialog.vue';
import PredictionDialog from '@/views/dashboard/home/components/PredictionDialog.vue';

import ChevronDown from '~icons/nnz/chevron-down';

import PollHistoryCard from './components/PollHistoryCard.vue';
import PollsLeadPanel from './components/PollsLeadPanel.vue';
import { filterByKind, historyKinds, splitByVotes, type HistoryKind } from './pollHistory';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const history = usePollHistory(channelId);
const pollsQuery = usePolls(channelId);
const predictionsQuery = usePredictions(channelId);
const kind = useLocalStorage<HistoryKind>('nnz.polls.kind', 'all', {
  serializer: {
    read: (raw: string): HistoryKind => historyKinds.find((value) => value === raw) ?? 'all',
    write: (value: HistoryKind): string => value,
  },
});

const pollOpen = ref(false);
const predictionOpen = ref(false);
const showEmpty = ref(false);

const runningPoll = computed<Poll | null>(
  () => (pollsQuery.data.value ?? []).find(isPollRunning) ?? null,
);
const runningPrediction = computed<Prediction | null>(
  () => (predictionsQuery.data.value ?? []).find(isPredictionRunning) ?? null,
);

// The lead panel shows what runs, or offers a start; with nothing running and no start allowed it is empty.
const showLead = computed(
  () =>
    runningPoll.value !== null ||
    runningPrediction.value !== null ||
    can('live-ops:polls:write') ||
    can('live-ops:predictions:write'),
);

const kindOptions = computed(() =>
  historyKinds.map((value) => ({
    value,
    label: t(`dashboard.polls.kinds.${value}`),
  })),
);

// The running one is the lead panel's; it joins the history when it ends.
const finished = computed<PollHistoryItem[]>(() => {
  const runningIds = [runningPoll.value?.id, runningPrediction.value?.id];

  return filterByKind(history.data.value ?? [], kind.value).filter(
    (item) => !runningIds.includes(item.id),
  );
});
const split = computed(() => splitByVotes(finished.value));
const state = usePanelState(history, () =>
  finished.value.length === 0);
</script>

<template>
  <PageHeader :title="t('dashboard.polls.title')" />
  <PageBody>
    <PollsLeadPanel
      v-if="showLead"
      :poll="runningPoll"
      :prediction="runningPrediction"
      @start-poll="pollOpen = true"
      @start-prediction="predictionOpen = true"
      @open-prediction="predictionOpen = true"
    />
    <PanelCard
      v-if="can('live-ops:polls:read')"
      bare
      :title="t('dashboard.polls.historyTitle')"
      :state="state"
      :empty-text="t('dashboard.polls.empty')"
      :error-text="t('dashboard.polls.error')"
      @retry="history.refetch()"
    >
      <template #actions>
        <SegmentedControl
          v-model="kind"
          :label="t('dashboard.polls.filterLabel')"
          :options="kindOptions"
        />
      </template>
      <div class="flex flex-col gap-3">
        <ul class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          <PollHistoryCard
            v-for="item in split.voted"
            :key="`${item.kind}:${item.id}`"
            :item="item"
          />
        </ul>
        <div v-if="split.empty.length > 0" class="flex flex-col gap-3">
          <Button
            data-zero-votes-toggle
            variant="ghost"
            size="sm"
            class="self-start"
            :aria-expanded="showEmpty"
            @click="showEmpty = !showEmpty"
          >
            {{ t("dashboard.polls.noVotes", { count: split.empty.length }, split.empty.length) }}
            <ChevronDown class="transition-transform" :class="showEmpty ? 'rotate-180' : ''" />
          </Button>
          <ul v-if="showEmpty" class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            <PollHistoryCard
              v-for="item in split.empty"
              :key="`${item.kind}:${item.id}`"
              :item="item"
            />
          </ul>
        </div>
      </div>
    </PanelCard>
    <PollDialog v-model:open="pollOpen" :poll="runningPoll" />
    <PredictionDialog v-model:open="predictionOpen" :prediction="runningPrediction" />
  </PageBody>
</template>
