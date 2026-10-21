<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The OBS page: is OBS linked, which scene is on air, and the controls a streamer needs while live.
// The connection sits on one line at the top of every tab. The Live tab holds only what is used on air;
// the studio tools and the overlay setup each have their own tab, so no view grows into a wall of panels.
// Dots: green works, orange needs a look (nothing loaded, OBS silent), red failed. The header dot is the worst of them.
import { useDocumentVisibility } from '@vueuse/core';
import { computed, useTemplateRef } from 'vue';
import { useI18n } from 'vue-i18n';

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
import { boardColumns } from '@/components/layout/board';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import type { PageTab } from '@/components/layout/pageTab';
import ReadOnlyScope from '@/components/layout/ReadOnlyScope.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useWidth } from '@/composables/useLayout';
import { usePageView } from '@/composables/usePageView';
import { usePanelState } from '@/composables/usePanelState';
import { keysOf } from '@/lib/array';
import { overlayHealth, type HealthDot } from '@/views/dashboard/overlays/components/overlayHealth';

import AudioPanel from './components/AudioPanel.vue';
import BrowserSourcesPanel from './components/BrowserSourcesPanel.vue';
import ObsConnectionBar from './components/ObsConnectionBar.vue';
import ObsHotkeysPanel from './components/ObsHotkeysPanel.vue';
import ObsScreenshotPanel from './components/ObsScreenshotPanel.vue';
import ObsStatusBar from './components/ObsStatusBar.vue';
import OutputsPanel from './components/OutputsPanel.vue';
import RequiredOverlaysPanel from './components/RequiredOverlaysPanel.vue';
import ScenesPanel from './components/ScenesPanel.vue';
import SourcesPanel from './components/SourcesPanel.vue';
import StudioPanel from './components/StudioPanel.vue';
import { useObsClocks } from './obsClocks';
import { bridgeHealth, connectionHealth, overallWords, worstDot } from './obsHealth';

const { t } = useI18n();
const { channelId } = useCurrentChannel();

const access = useChannelAccess(channelId);
const canRead = computed<boolean>(() => access.can('obs:scene:read'));
const canControl = computed<boolean>(() => access.can('obs:control'));
const canSeeConnection = computed<boolean>(() => access.can('obs:config:read'));
const canSeeSources = computed<boolean>(() => access.can('widget:read'));

// Each tab opens with the read key of the panels on it; the most panels side by side it ever needs.
const tabKeys = {
  live: { read: 'obs:scene:read', columns: 3 },
  tools: { read: 'obs:scene:read', columns: 3 },
  overlays: { read: 'widget:read', columns: 2 },
} as const;
const VIEWS = keysOf(tabKeys);

type ObsTab = (typeof VIEWS)[number];
const allTabs = computed<PageTab<ObsTab>[]>(() => [
  { value: 'live', label: t('dashboard.obs.tabs.live') },
  { value: 'tools', label: t('dashboard.obs.tabs.tools') },
  { value: 'overlays', label: t('dashboard.obs.tabs.overlays') },
]);
const tabs = computed(() => allTabs.value.filter((tab) => access.can(tabKeys[tab.value].read)));
const view = usePageView(VIEWS, 'view', () =>
  tabs.value.map((tab) => tab.value));

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
const widgets = useWidgets(channelId);
const required = useRequiredOverlays(channelId, canSeeSources);
const requiredPanel = usePanelState(required);

// The required overlays have their own panel; the browser sources are the user's own.
const ownSources = computed<Widget[] | undefined>(() =>
  widgets.data.value
    ? withoutRequired(widgets.data.value, required.data.value)
    : undefined,
);

// A panel needs about 24rem; the page counts how many columns fit its measured width, up to the tab's limit.
const panelMinimum = 384;
const page = useTemplateRef('page');
const width = useWidth(page);
const columns = computed<number>(() =>
  boardColumns(width.value, panelMinimum, tabKeys[view.value].columns));
const boardStyle = computed(() => ({ gridTemplateColumns: `repeat(${columns.value}, minmax(0, 1fr))` }));

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

// Green and gray keep their plain words; orange and red name what is not loaded or what failed.
const overallText = computed<string>(() => {
  if (overall.value === 'warn' || overall.value === 'bad') {
    const words = overallWords(
      connectionHealth(summary.value, unreachable.value),
      bridgeHealth(summary.value, bridge.data.value),
      Number(bridge.data.value?.instanceCount ?? 0),
      widgets.data.value ?? [],
    );

    return t(words.key, { count: words.count ?? 0 }, words.count ?? 0);
  }

  return t(`dashboard.obs.overall.${overall.value || 'off'}`);
});

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
        {{ overallText }}
      </span>
    </template>
    <template v-if="tabs.length > 1" #bar>
      <PageTabs :tabs="tabs" />
    </template>
  </PageHeader>
  <PageBody>
    <div ref="page" data-page-inset class="flex flex-1 flex-col gap-3 @container">
      <ObsConnectionBar
        v-if="canSeeConnection"
        :connection="connection.data.value"
        :bridge="bridge.data.value"
        :unreachable="unreachable"
        :panel-state="connectionPanel"
        @retry="retry"
      />
      <template v-if="view !== 'overlays' && canRead">
        <ObsStatusBar
          v-if="view === 'live' && enabled"
          :stats="stats.data.value"
          :read-at="stats.dataUpdatedAt.value"
          :live="clocks.live.value"
          :stream-timer="clocks.stream.value"
          :record-timer="clocks.record.value"
        />
        <p v-if="!canControl" data-obs-readonly-note class="text-sm text-muted-foreground">
          {{ $t("dashboard.obs.needsControl") }}
        </p>
        <ReadOnlyScope
          :class="view === 'live' ? 'flex flex-1 flex-col' : ''"
          :read-only="!canControl"
          :reason="$t('dashboard.obs.needsControl')"
        >
          <!-- The Live bento spans the page: outputs and scenes on top, the sources under them, the tall audio
               mixer beside them. The outputs set the top row; the other panels scroll inside their cells. -->
          <div
            v-if="view === 'live'"
            data-obs-live
            data-obs-board
            class="grid flex-1 gap-3 [grid-template-areas:'outputs'_'scenes'_'audio'_'sources'] @min-[48rem]:grid-cols-2 @min-[48rem]:grid-rows-[auto_1fr] @min-[48rem]:[grid-template-areas:'outputs_scenes'_'audio_sources'] @min-[74rem]:grid-cols-[minmax(0,1.3fr)_minmax(0,1fr)_minmax(0,1fr)] @min-[74rem]:grid-rows-[auto_1fr] @min-[74rem]:[grid-template-areas:'outputs_scenes_audio'_'sources_sources_audio']"
          >
            <OutputsPanel
              class="[grid-area:outputs]"
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
              class="[grid-area:scenes] @min-[48rem]:[contain:size]"
              :scenes="scenes.data.value"
              :panel-state="scenesPanel"
              :offline="!enabled"
              @retry="retry"
            />
            <AudioPanel class="[grid-area:audio] @min-[48rem]:[contain:size]" :offline="!enabled" />
            <SourcesPanel
              class="[grid-area:sources] @min-[48rem]:[contain:size]"
              :scene-name="state.data.value?.currentScene ?? null"
              :offline="!enabled"
            />
          </div>
          <div v-else data-obs-board class="board-lanes items-start gap-3" :style="boardStyle">
            <StudioPanel v-if="enabled" :scenes="scenes.data.value" />
            <ObsHotkeysPanel :offline="!enabled" />
            <ObsScreenshotPanel :offline="!enabled" />
          </div>
        </ReadOnlyScope>
      </template>
      <div
        v-else-if="view === 'overlays' && canSeeSources"
        data-obs-overlays
        class="board-lanes items-start gap-3"
        :style="boardStyle"
      >
        <RequiredOverlaysPanel
          :overlays="required.data.value"
          :panel-state="requiredPanel"
          @retry="required.refetch()"
        />
        <BrowserSourcesPanel
          :widgets="ownSources"
          :panel-state="sourcesPanel"
          @retry="widgets.refetch()"
        />
      </div>
    </div>
  </PageBody>
</template>
