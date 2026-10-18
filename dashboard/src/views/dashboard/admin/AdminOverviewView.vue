<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The platform admin's first page: the numbers of the whole deployment, read from admin/stats and
// refreshed on its own. Only a system status other than healthy takes the warning color.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useAdminStats } from '@/api/adminOverview';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatStrip from '@/components/layout/StatStrip.vue';
import StatTile from '@/components/layout/StatTile.vue';
import { usePanelState } from '@/composables/usePanelState';

const SECONDS_PER_MINUTE: number = 60;
const SECONDS_PER_HOUR: number = 3600;
const SECONDS_PER_DAY: number = 86_400;

const { t } = useI18n();

const stats = useAdminStats();
const panelState = usePanelState(stats, (data) =>
  data === null);

const healthy = computed<boolean>(() => stats.data.value?.systemStatus === 'healthy');

const statusLabel = computed<string>(() => {
  const status = stats.data.value?.systemStatus ?? '';

  return t(`dashboard.admin.overview.status.${status}`, status);
});

const uptime = computed<string>(() => {
  const seconds = Number(stats.data.value?.botUptimeSeconds ?? 0);

  return t('dashboard.admin.overview.uptimeValue', {
    days: Math.floor(seconds / SECONDS_PER_DAY),
    hours: Math.floor((seconds % SECONDS_PER_DAY) / SECONDS_PER_HOUR),
    minutes: Math.floor((seconds % SECONDS_PER_HOUR) / SECONDS_PER_MINUTE),
  });
});
</script>

<template>
  <PageHeader :title="$t('dashboard.admin.overview.title')" :crumb="$t('dashboard.admin.crumb')" />
  <PageBody>
    <PanelCard
      tier="lead"
      :title="$t('dashboard.admin.overview.panel')"
      :description="$t('dashboard.admin.overview.refresh')"
      :state="panelState"
      :empty-text="$t('dashboard.admin.overview.empty')"
      :error-text="$t('dashboard.admin.overview.error')"
      @retry="stats.refetch()"
    >
      <StatStrip v-if="stats.data.value">
        <StatTile
          :label="$t('dashboard.admin.overview.channels')"
          :value="Number(stats.data.value.totalChannels)"
          :context="$t('dashboard.admin.overview.channelsContext')"
        />
        <StatTile
          :label="$t('dashboard.admin.overview.active')"
          :value="Number(stats.data.value.activeChannels)"
          :context="$t('dashboard.admin.overview.activeContext')"
        />
        <StatTile
          :label="$t('dashboard.admin.overview.users')"
          :value="Number(stats.data.value.totalUsers)"
          :context="$t('dashboard.admin.overview.usersContext')"
        />
        <StatTile
          :label="$t('dashboard.admin.overview.system')"
          :value="statusLabel"
          :warn="!healthy"
          :context="$t('dashboard.admin.overview.systemContext')"
        />
        <StatTile
          :label="$t('dashboard.admin.overview.uptime')"
          :value="uptime"
          :context="$t('dashboard.admin.overview.uptimeContext')"
        />
        <StatTile
          :label="$t('dashboard.admin.overview.events')"
          :value="Number(stats.data.value.eventsProcessedToday)"
          :context="$t('dashboard.admin.overview.eventsContext')"
        />
      </StatStrip>
    </PanelCard>
    <p class="text-sm text-muted-foreground">
      {{ $t('dashboard.admin.overview.restricted') }}
    </p>
  </PageBody>
</template>
