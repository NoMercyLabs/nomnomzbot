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

import BoardCell from '@/components/layout/BoardCell.vue';
import BoardGrid from '@/components/layout/BoardGrid.vue';
import type { BoardPanel } from '@/components/layout/board';
import PageHeader from '@/components/layout/PageHeader.vue';

import RedemptionsPanel from './components/RedemptionsPanel.vue';
import RewardsPanel from './components/RewardsPanel.vue';

const { t } = useI18n();

const panels = computed(() => [
  { id: 'redemptions', label: t('dashboard.channelPoints.redemptions.title') },
  { id: 'rewards', label: t('dashboard.channelPoints.rewards.title') },
] as const satisfies readonly BoardPanel[]);
</script>

<template>
  <PageHeader :title="$t('dashboard.channelPoints.title')" :crumb="$t('dashboard.channelPoints.crumb')" />
  <BoardGrid :panels="panels" :rows="1">
    <BoardCell panel="redemptions" :span="6">
      <RedemptionsPanel />
    </BoardCell>
    <BoardCell panel="rewards" :span="6">
      <RewardsPanel />
    </BoardCell>
  </BoardGrid>
</template>
