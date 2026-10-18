<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Your requests so far (export, erasure, opt-out), newest first, each with where it stands.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useDataRequests } from '@/api/yourData';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatusBadge, { type StatusTone } from '@/components/layout/StatusBadge.vue';
import { usePanelState } from '@/composables/usePanelState';
import { lookup } from '@/lib/array';
import { shortDate } from '@/lib/time';

const { t, te, locale } = useI18n();
const requests = useDataRequests();

const state = usePanelState(requests, (rows) =>
  rows.length === 0);
const rows = computed(() => requests.data.value ?? []);

const tones = {
  completed: 'ok',
  failed: 'bad',
} as const satisfies Record<string, StatusTone>;

function tone(status: string): StatusTone {
  return lookup(tones, status) ?? 'off';
}

function word(group: string, key: string): string {
  const path = `dashboard.settings.yourData.requests.${group}.${key}`;

  return te(path)
    ? t(path)
    : key.replaceAll('_', ' ');
}
</script>

<template>
  <PanelCard
    data-panel="requests"
    tier="quiet"
    :title="$t('dashboard.settings.yourData.requests.title')"
    :description="$t('dashboard.settings.yourData.requests.description')"
    :state="state"
    :empty-text="$t('dashboard.settings.yourData.requests.empty')"
    :error-text="$t('dashboard.settings.yourData.requests.loadFailed')"
    @retry="requests.refetch()"
  >
    <RowGroup>
      <div
        v-for="request in rows"
        :key="request.id"
        :data-request="request.id"
        class="flex min-h-11 flex-wrap items-center gap-3 rounded-[8px] px-3 py-2"
      >
        <div class="min-w-0 flex-1">
          <span class="block text-sm font-medium">{{ word('type', request.requestType) }}</span>
          <span class="block text-sm text-muted-foreground">
            {{
              $t('dashboard.settings.yourData.requests.meta', {
                date: shortDate(request.requestedAt, locale),
                scope: word('scope', request.scope),
              })
            }}
          </span>
          <span v-if="request.failureReason" data-failure class="block text-sm text-destructive">
            {{ request.failureReason }}
          </span>
        </div>
        <StatusBadge :tone="tone(request.status)" :label="word('status', request.status)" />
      </div>
    </RowGroup>
  </PanelCard>
</template>
