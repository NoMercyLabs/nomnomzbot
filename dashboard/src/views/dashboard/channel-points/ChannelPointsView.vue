<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
