<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The stat strip and the status badges.
import { useI18n } from 'vue-i18n';

import StatStrip from '@/components/layout/StatStrip.vue';
import StatTile from '@/components/layout/StatTile.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';

const { t } = useI18n();
</script>

<template>
  <section data-catalog="stats" class="grid gap-4">
    <StatStrip>
      <StatTile
        :label="t('dashboard.catalog.stats.messages')"
        :value="54431"
        :context="t('dashboard.catalog.stats.messagesContext')"
      />
      <StatTile
        :label="t('dashboard.catalog.stats.channels')"
        :value="26"
        :context="t('dashboard.catalog.stats.channelsContext')"
      />
      <StatTile
        :label="t('dashboard.catalog.stats.hidden')"
        :value="8019"
        :context="t('dashboard.catalog.stats.hiddenContext')"
        warn
      />
    </StatStrip>

    <div class="flex flex-wrap gap-4">
      <StatusBadge tone="ok" :label="t('dashboard.catalog.status.ok')" />
      <StatusBadge tone="bad" :label="t('dashboard.catalog.status.bad')" />
      <StatusBadge tone="live" :label="t('dashboard.catalog.status.live')" />
      <StatusBadge tone="off" :label="t('dashboard.catalog.status.off')" />
    </div>
  </section>
</template>
