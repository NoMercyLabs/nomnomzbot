<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Every event of the current stream and of the stream before it, each one replayable.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useActivity, useHomeStats, useTwoStreamsActivity, type ActivityEvent } from '@/api/home';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import ActivityRows from './components/ActivityRows.vue';
import { splitByStream } from './splitByStream';

const { t } = useI18n();
const { channelId } = useCurrentChannel();

const stats = useHomeStats(channelId);
const newest = useActivity(channelId);
const both = useTwoStreamsActivity(channelId);

const live = computed<boolean>(() => stats.data.value?.isLive === true);

// The newest stream's events are the home feed; whatever else the two-stream window holds is the stream before.
const split = computed(() => splitByStream(newest.data.value ?? [], both.data.value ?? []));

const newestState = usePanelState(newest, (items: ActivityEvent[]) =>
  items.length === 0);
const earlierState = usePanelState(both, () =>
  split.value.earlier.length === 0);

function retry(): void {
  void newest.refetch();
  void both.refetch();
}
</script>

<template>
  <PageHeader :title="t('dashboard.events.title')" />
  <PageBody>
    <PanelCard
      tier="lead"
      :title="live ? t('dashboard.events.thisStream') : t('dashboard.events.lastStream')"
      :state="newestState"
      :empty-text="t('dashboard.events.empty')"
      :error-text="t('dashboard.events.error')"
      @retry="retry"
    >
      <ActivityRows :events="split.newest" />
    </PanelCard>
    <PanelCard
      :title="live ? t('dashboard.events.lastStream') : t('dashboard.events.streamBefore')"
      :state="earlierState"
      :empty-text="t('dashboard.events.emptyEarlier')"
      :error-text="t('dashboard.events.error')"
      @retry="retry"
    >
      <ActivityRows :events="split.earlier" />
    </PanelCard>
  </PageBody>
</template>
