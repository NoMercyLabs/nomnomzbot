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

  return t(`dashboard.adminOverview.status.${status}`, status);
});

const uptime = computed<string>(() => {
  const seconds = Number(stats.data.value?.botUptimeSeconds ?? 0);

  return t('dashboard.adminOverview.uptimeValue', {
    days: Math.floor(seconds / SECONDS_PER_DAY),
    hours: Math.floor((seconds % SECONDS_PER_DAY) / SECONDS_PER_HOUR),
    minutes: Math.floor((seconds % SECONDS_PER_HOUR) / SECONDS_PER_MINUTE),
  });
});
</script>

<template>
  <PageHeader :title="$t('dashboard.adminOverview.title')" :crumb="$t('dashboard.admin.crumb')" />
  <PageBody>
    <PanelCard
      tier="lead"
      :title="$t('dashboard.adminOverview.panel')"
      :description="$t('dashboard.adminOverview.refresh')"
      :state="panelState"
      :empty-text="$t('dashboard.adminOverview.empty')"
      :error-text="$t('dashboard.adminOverview.error')"
      @retry="stats.refetch()"
    >
      <StatStrip v-if="stats.data.value">
        <StatTile
          :label="$t('dashboard.adminOverview.channels')"
          :value="Number(stats.data.value.totalChannels)"
          :context="$t('dashboard.adminOverview.channelsContext')"
        />
        <StatTile
          :label="$t('dashboard.adminOverview.active')"
          :value="Number(stats.data.value.activeChannels)"
          :context="$t('dashboard.adminOverview.activeContext')"
        />
        <StatTile
          :label="$t('dashboard.adminOverview.users')"
          :value="Number(stats.data.value.totalUsers)"
          :context="$t('dashboard.adminOverview.usersContext')"
        />
        <StatTile
          :label="$t('dashboard.adminOverview.system')"
          :value="statusLabel"
          :warn="!healthy"
          :context="$t('dashboard.adminOverview.systemContext')"
        />
        <StatTile
          :label="$t('dashboard.adminOverview.uptime')"
          :value="uptime"
          :context="$t('dashboard.adminOverview.uptimeContext')"
        />
        <StatTile
          :label="$t('dashboard.adminOverview.events')"
          :value="Number(stats.data.value.eventsProcessedToday)"
          :context="$t('dashboard.adminOverview.eventsContext')"
        />
      </StatStrip>
    </PanelCard>
    <p class="text-sm text-muted-foreground">
      {{ $t('dashboard.adminOverview.restricted') }}
    </p>
  </PageBody>
</template>
