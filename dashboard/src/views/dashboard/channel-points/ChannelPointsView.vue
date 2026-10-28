<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useRewardAvailability } from '@/api/rewards';
import BoardCell from '@/components/layout/BoardCell.vue';
import BoardGrid from '@/components/layout/BoardGrid.vue';
import type { BoardPanel } from '@/components/layout/board';
import PageHeader from '@/components/layout/PageHeader.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import RedemptionsPanel from './components/RedemptionsPanel.vue';
import RewardsPanel from './components/RewardsPanel.vue';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const availability = useRewardAvailability(channelId);

// Only a known "no" turns channel points off; while the answer loads the page stays as it was.
const unavailable = computed(() =>
  availability.data.value?.channelPointsAvailable === false);

const panels = computed((): BoardPanel[] => {
  const rewards = { id: 'rewards', label: t('dashboard.channelPoints.rewards.title') };

  if (unavailable.value)
    return [rewards];

  return [{ id: 'redemptions', label: t('dashboard.channelPoints.redemptions.title') }, rewards];
});
</script>

<template>
  <PageHeader :title="$t('dashboard.channelPoints.title')" :crumb="$t('dashboard.channelPoints.crumb')" />
  <p
    v-if="unavailable"
    role="status"
    class="rounded-lg border bg-muted/50 px-4 py-3 text-sm text-muted-foreground"
    data-channel-points-unavailable
  >
    {{ $t('dashboard.channelPoints.unavailable') }}
  </p>
  <BoardGrid :panels="panels" :rows="1">
    <BoardCell v-if="!unavailable" panel="redemptions" :span="6">
      <RedemptionsPanel />
    </BoardCell>
    <BoardCell panel="rewards" :span="unavailable ? 12 : 6">
      <RewardsPanel />
    </BoardCell>
  </BoardGrid>
</template>
