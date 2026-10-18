<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The OBS link in three rows: the saved connection, the browser-source bridge, and what OBS is doing now.
// Each row has a dot (green works, orange needs a look, red failed, gray off) and the reason in words.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { ObsBridgeStatus, ObsConnection, ObsState } from '@/api/obs';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { useCan } from '@/composables/useCan';

import ObsConnectionForm from './ObsConnectionForm.vue';
import { bridgeHealth, connectionHealth, type Health } from '../obsHealth';

const { can } = useCan();

const props = defineProps<{
  connection: ObsConnection | null | undefined;
  bridge: ObsBridgeStatus | null | undefined;
  state: ObsState | null | undefined;
  unreachable: boolean;
  panelState: PanelState;
}>();

defineEmits<{ retry: [] }>();

const { t } = useI18n();

const link = computed<Health>(() => connectionHealth(props.connection, props.unreachable));
const bridgeRow = computed<Health>(() => bridgeHealth(props.connection, props.bridge));

// The reason a connection failed, or its address, when the bot has them.
const linkMeta = computed<string | undefined>(() => {
  if (link.value.dot === 'bad')
    return props.connection?.lastError ?? undefined;

  return props.connection?.host
    ? `${props.connection.host}:${props.connection.port ?? ''}`
    : undefined;
});

const bridgeDetail = computed<string>(() =>
  t(`dashboard.obs.${bridgeRow.value.reason}`, { count: Number(props.bridge?.instanceCount ?? 0) }),
);

const liveText = computed<string>(() =>
  t(props.state?.streaming
    ? 'dashboard.obs.live.streaming'
    : 'dashboard.obs.live.notStreaming'),
);
const recordingText = computed<string | undefined>(() =>
  props.state?.recording
    ? t('dashboard.obs.live.recording')
    : undefined,
);
</script>

<template>
  <PanelCard
    :title="$t('dashboard.obs.connection.title')"
    :state="panelState"
    :error-text="$t('dashboard.obs.connection.loadError')"
    @retry="$emit('retry')"
  >
    <div data-obs-connection class="px-1 py-0.5">
      <SummaryRow
        :title="$t('dashboard.obs.connection.obs')"
        :detail="$t(`dashboard.obs.${link.reason}`)"
        :meta="linkMeta"
      >
        <template #detail-lead>
          <span data-obs-dot="connection" class="dot" :class="link.dot" aria-hidden="true" />
        </template>
      </SummaryRow>
      <SummaryRow :title="$t('dashboard.obs.bridge.title')" :detail="bridgeDetail">
        <template #detail-lead>
          <span data-obs-dot="bridge" class="dot" :class="bridgeRow.dot" aria-hidden="true" />
        </template>
      </SummaryRow>
      <SummaryRow
        v-if="state"
        data-obs-live
        :title="$t('dashboard.obs.live.title')"
        :detail="liveText"
        :meta="recordingText"
      />
    </div>
    <ObsConnectionForm v-if="can('obs:config:write')" :connection="connection" />
  </PanelCard>
</template>
