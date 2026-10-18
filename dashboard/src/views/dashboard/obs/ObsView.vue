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
import { useDocumentVisibility, useLocalStorage } from '@vueuse/core';
import { computed, useTemplateRef } from 'vue';

import { useChannelAccess } from '@/api/access';
import {
  useObsBridge,
  useObsConnection,
  useObsConnectionStatus,
  useObsScenes,
  useObsState,
} from '@/api/obs';
import { useObsStats, useObsVirtualCam } from '@/api/obsOutputs';
import { useRequiredOverlays, useWidgets, withoutRequired, type Widget } from '@/api/widgets';
import PageBody from '@/components/layout/PageBody.vue';
import { boardColumns } from '@/components/layout/board';
import PageHeader from '@/components/layout/PageHeader.vue';
import { Button } from '@/components/ui/button';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useWidth } from '@/composables/useLayout';
import { usePanelState } from '@/composables/usePanelState';
import { overlayHealth, type HealthDot } from '@/views/dashboard/overlays/components/overlayHealth';

import AudioPanel from './components/AudioPanel.vue';
import BrowserSourcesPanel from './components/BrowserSourcesPanel.vue';
import ObsConnectionPanel from './components/ObsConnectionPanel.vue';
import ObsHotkeysPanel from './components/ObsHotkeysPanel.vue';
import ObsScreenshotPanel from './components/ObsScreenshotPanel.vue';
import OutputsPanel from './components/OutputsPanel.vue';
import RequiredOverlaysPanel from './components/RequiredOverlaysPanel.vue';
import ScenesPanel from './components/ScenesPanel.vue';
import SourcesPanel from './components/SourcesPanel.vue';
import StudioPanel from './components/StudioPanel.vue';
import ObsStatusBar from './components/ObsStatusBar.vue';
import { useObsClocks } from './obsClocks';
import { bridgeHealth, connectionHealth, worstDot } from './obsHealth';

const { channelId } = useCurrentChannel();

const access = useChannelAccess(channelId);
const canSeeConnection = computed<boolean>(() => access.can('obs:config:read'));

// The configuration and the bridge need the configuration key; the status needs only the control key.
const configChannelId = computed<string | null>(() =>
  canSeeConnection.value
    ? channelId.value
    : null,
);
const connection = useObsConnection(configChannelId);
const bridge = useObsBridge(configChannelId);
const status = useObsConnectionStatus(channelId);
const summary = computed(() => connection.data.value ?? status.data.value);
const enabled = computed<boolean>(() => summary.value?.isEnabled === true);
const state = useObsState(channelId, enabled);
const scenes = useObsScenes(channelId, enabled);
const canSeeSources = computed<boolean>(() => access.can('widget:read'));
const widgets = useWidgets(channelId);
const required = useRequiredOverlays(channelId, canSeeSources);
const requiredPanel = usePanelState(required);

// The required overlays have their own panel; the browser sources are the user's own.
const ownSources = computed<Widget[] | undefined>(() =>
  widgets.data.value
    ? withoutRequired(widgets.data.value, required.data.value)
    : undefined,
);

// The live panels each need about 24rem; the page counts how many columns fit its measured width, up to three.
// The setup panels (connection, required overlays, browser sources) are not a live task: they sit folded away,
// and the page remembers whether the streamer opened them.
const liveMinimum = 384;
const live = useTemplateRef('live');
const width = useWidth(live);
const columns = computed<number>(() => boardColumns(width.value, liveMinimum, 3));
const setupOpen = useLocalStorage<boolean>('nnz.obs.setup', false);

// The stats poll runs only while this tab is on screen.
const visibility = useDocumentVisibility();
const visible = computed<boolean>(() => visibility.value === 'visible');
const stats = useObsStats(channelId, enabled, visible, 2000);
const virtualCam = useObsVirtualCam(channelId, enabled);
const clocks = useObsClocks(() => ({
  stats: stats.data.value,
  statsAt: stats.dataUpdatedAt.value,
  state: state.data.value,
  stateAt: state.dataUpdatedAt.value,
}));
const outputsPanel = usePanelState(state);

const connectionPanel = usePanelState(connection);
const sourcesPanel = usePanelState(widgets);
const scenesPanel = usePanelState(scenes);

// OBS is switched on but a live read was refused: it is not reachable.
const refused = computed<boolean>(() => state.isError.value || scenes.isError.value);
const unreachable = computed<boolean>(() => enabled.value && refused.value);

// The full widget list already holds the required overlays, so the header dot covers both panels.
const overall = computed<HealthDot>(() => {
  const sources: readonly Widget[] = widgets.data.value ?? [];

  return worstDot([
    connectionHealth(summary.value, unreachable.value).dot,
    bridgeHealth(summary.value, bridge.data.value).dot,
    ...sources.map((widget) => overlayHealth(widget)),
  ]);
});

const overallReason = computed<string>(() => `dashboard.obs.overall.${overall.value || 'off'}`);

function retry(): void {
  void status.refetch();
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
    <div data-page-inset class="grid gap-3 @container">
      <ObsStatusBar
        v-if="enabled && access.can('obs:control')"
        :stats="stats.data.value"
        :read-at="stats.dataUpdatedAt.value"
        :live="clocks.live.value"
        :stream-timer="clocks.stream.value"
        :record-timer="clocks.record.value"
      />
      <div
        ref="live"
        data-obs-board
        class="grid items-start gap-3"
        :style="{ gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` }"
      >
        <OutputsPanel
          v-if="access.can('obs:control')"
          :channel-id="channelId"
          :state="state.data.value"
          :virtual-cam="virtualCam.data.value === true"
          :stream-timer="clocks.stream.value"
          :record-timer="clocks.record.value"
          :panel-state="outputsPanel"
          :offline="!enabled"
          @retry="retry"
        />
        <ScenesPanel
          v-if="access.can('obs:control')"
          :scenes="scenes.data.value"
          :panel-state="scenesPanel"
          :offline="!enabled"
          @retry="retry"
        />
        <AudioPanel v-if="access.can('obs:control')" :offline="!enabled" />
        <StudioPanel v-if="enabled && access.can('obs:control')" :scenes="scenes.data.value" />
        <ObsHotkeysPanel v-if="access.can('obs:control')" :offline="!enabled" />
        <ObsScreenshotPanel v-if="access.can('obs:control')" :offline="!enabled" />
        <SourcesPanel
          v-if="access.can('obs:control')"
          :scene-name="state.data.value?.currentScene ?? null"
          :offline="!enabled"
        />
      </div>
      <Collapsible v-if="canSeeSources || canSeeConnection" v-model:open="setupOpen" data-obs-setup>
        <CollapsibleTrigger as-child>
          <Button variant="ghost" size="sm" class="text-muted-foreground">
            {{ $t(setupOpen ? "dashboard.obs.setup.hide" : "dashboard.obs.setup.show") }}
          </Button>
        </CollapsibleTrigger>
        <CollapsibleContent>
          <div
            class="mt-2 grid items-start gap-3"
            :style="{ gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` }"
          >
            <ObsConnectionPanel
              v-if="canSeeConnection"
              :connection="connection.data.value"
              :bridge="bridge.data.value"
              :state="state.data.value"
              :unreachable="unreachable"
              :panel-state="connectionPanel"
              @retry="retry"
            />
            <RequiredOverlaysPanel
              v-if="canSeeSources"
              :overlays="required.data.value"
              :panel-state="requiredPanel"
              @retry="required.refetch()"
            />
            <BrowserSourcesPanel
              v-if="access.can('widget:read')"
              :widgets="ownSources"
              :panel-state="sourcesPanel"
              @retry="widgets.refetch()"
            />
          </div>
        </CollapsibleContent>
      </Collapsible>
    </div>
  </PageBody>
</template>
