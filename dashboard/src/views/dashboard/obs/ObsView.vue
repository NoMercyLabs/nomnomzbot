<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The OBS page: is OBS linked, is every overlay loaded in it, and which scene is on air.
// Dots: green works, orange needs a look (nothing loaded, OBS silent), red failed. The header dot is the worst of them.
import { useDocumentVisibility } from '@vueuse/core';
import { computed } from 'vue';

import { useObsBridge, useObsConnection, useObsScenes, useObsState } from '@/api/obs';
import { useObsStats, useObsVirtualCam } from '@/api/obsOutputs';
import { useWidgets, type Widget } from '@/api/widgets';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { overlayHealth, type HealthDot } from '@/views/dashboard/overlays/components/overlayHealth';

import BrowserSourcesPanel from './components/BrowserSourcesPanel.vue';
import ObsConnectionPanel from './components/ObsConnectionPanel.vue';
import OutputsPanel from './components/OutputsPanel.vue';
import ScenesPanel from './components/ScenesPanel.vue';
import StudioPanel from './components/StudioPanel.vue';
import StatsStrip from './components/StatsStrip.vue';
import { bridgeHealth, connectionHealth, worstDot } from './obsHealth';

const { channelId } = useCurrentChannel();

const connection = useObsConnection(channelId);
const bridge = useObsBridge(channelId);
const enabled = computed<boolean>(() => connection.data.value?.isEnabled === true);
const state = useObsState(channelId, enabled);
const scenes = useObsScenes(channelId, enabled);
const widgets = useWidgets(channelId);

// The stats poll runs only while this tab is on screen.
const visibility = useDocumentVisibility();
const visible = computed<boolean>(() => visibility.value === 'visible');
const stats = useObsStats(channelId, enabled, visible, 5000);
const virtualCam = useObsVirtualCam(channelId, enabled);
const outputsPanel = usePanelState(state);

const connectionPanel = usePanelState(connection);
const sourcesPanel = usePanelState(widgets);
const scenesPanel = usePanelState(scenes);

// OBS is switched on but a live read was refused: it is not reachable.
const refused = computed<boolean>(() => state.isError.value || scenes.isError.value);
const unreachable = computed<boolean>(() => enabled.value && refused.value);

const overall = computed<HealthDot>(() => {
  const sources: readonly Widget[] = widgets.data.value ?? [];

  return worstDot([
    connectionHealth(connection.data.value, unreachable.value).dot,
    bridgeHealth(connection.data.value, bridge.data.value).dot,
    ...sources.map((widget: Widget) => overlayHealth(widget)),
  ]);
});

const overallReason = computed<string>(() => `dashboard.obs.overall.${overall.value || 'off'}`);

function retry(): void {
  void connection.refetch();
  void bridge.refetch();
  void state.refetch();
  void scenes.refetch();
}
</script>

<template>
  <PageHeader :title="$t('dashboard.obs.title')" :crumb="$t('dashboard.obs.crumb')">
    <template #status>
      <span data-obs-overall class="inline-flex items-center gap-2 text-sm text-muted-foreground">
        <span class="dot" :class="overall" aria-hidden="true" />
        {{ $t(overallReason) }}
      </span>
    </template>
  </PageHeader>
  <PageBody>
    <div data-page-inset class="grid gap-4 @container lg:grid-cols-2">
      <ObsConnectionPanel
        :connection="connection.data.value"
        :bridge="bridge.data.value"
        :state="state.data.value"
        :unreachable="unreachable"
        :panel-state="connectionPanel"
        @retry="retry"
      />
      <OutputsPanel
        :channel-id="channelId"
        :state="state.data.value"
        :virtual-cam="virtualCam.data.value === true"
        :panel-state="outputsPanel"
        :offline="!enabled"
        @retry="retry"
      >
        <StatsStrip :stats="stats.data.value" />
      </OutputsPanel>
      <ScenesPanel
        :scenes="scenes.data.value"
        :panel-state="scenesPanel"
        :offline="!enabled"
        @retry="retry"
      />
      <StudioPanel v-if="enabled" :scenes="scenes.data.value" />
      <BrowserSourcesPanel
        class="lg:col-span-2"
        :widgets="widgets.data.value"
        :panel-state="sourcesPanel"
        @retry="widgets.refetch()"
      />
    </div>
  </PageBody>
</template>
