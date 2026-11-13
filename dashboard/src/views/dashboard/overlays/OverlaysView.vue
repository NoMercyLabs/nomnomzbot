<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The Overlays page: every overlay of the channel as a list, tabs that filter it, and one overlay beside it.
// The overlay in view is the id in ?id=, so a link opens it; with no id nothing is open, like every other list page.
// Tab counts come from the loaded list: the bot has no count endpoint.
// The audio levels are channel-wide, not one overlay's, so they open from the header in their own drawer.
import VolumeTwo from '~icons/nnz/volume-two';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useRequiredOverlays, useWidgets, withoutRequired, type Widget } from '@/api/widgets';
import DetailEditorPane from '@/components/layout/DetailEditorPane.vue';
import ListDetail from '@/components/layout/ListDetail.vue';
import ListPanel from '@/components/layout/ListPanel.vue';
import PageCreateButton from '@/components/layout/PageCreateButton.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import PanelBeside from '@/components/layout/PanelBeside.vue';
import SidePanel from '@/components/layout/SidePanel.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useFilteredResourceList } from '@/composables/useFilteredResourceList';

import AudioMixPanel from './components/AudioMixPanel.vue';
import NewOverlayDialog from './components/NewOverlayDialog.vue';
import OverlayDetail from './components/OverlayDetail.vue';
import OverlayRow from './components/OverlayRow.vue';

const FILTERS = {
  all: () => true,
  on: (widget: Widget) => widget.isEnabled,
  off: (widget: Widget) => !widget.isEnabled,
  problem: (widget: Widget) => Boolean(widget.lastRuntimeError),
} as const satisfies Record<string, (widget: Widget) => boolean>;

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const widgets = useWidgets(channelId);
const audioOpen = ref(false);
const creating = ref(false);

const required = useRequiredOverlays(channelId);

// The required overlays are shown only on the OBS page; this list is the user's own choices.
const loaded = computed(() =>
  widgets.data.value
    ? withoutRequired(widgets.data.value, required.data.value)
    : undefined,
);

// An id in the address looks in every overlay, so a switched-off one stays open on the On tab.
const {
  view,
  tabs,
  search,
  visible,
  panelState,
  empty,
  count: shown,
  pane,
  asked,
  selectedId,
  selected,
} = useFilteredResourceList({
  query: widgets,
  items: () => loaded.value,
  filters: FILTERS,
  tabLabel: (tab) => t(`dashboard.overlays.tabs.${tab}`),
  searchText: (widget) => [widget.name],
  paneKey: 'id',
});

const EMPTY_KEYS = {
  none: 'dashboard.overlays.list.empty',
  filtered: 'dashboard.overlays.list.emptyTab',
} as const;

const emptyText = computed<string>(() =>
  empty.value.kind === 'noMatch'
    ? t('dashboard.overlays.noMatch', { query: empty.value.query })
    : t(EMPTY_KEYS[empty.value.kind]));

const hint = computed<string>(() => t(`dashboard.overlays.hints.${view.value}`));
const count = computed<string | undefined>(() =>
  shown.value === undefined
    ? undefined
    : t('dashboard.overlays.count', { count: shown.value }),
);

function pick(widget: Widget): void {
  pane.open(widget.id);
}

// A new overlay opens at once, so the person can start on its code.
function open(id: string | null): void {
  if (id !== null) {
    pane.open(id);
  }
}
</script>

<template>
  <PageHeader :title="$t('dashboard.overlays.title')" :crumb="$t('dashboard.overlays.crumb')">
    <template #actions>
      <Button v-if="can('sounds:read')" variant="outline" size="sm" @click="audioOpen = true">
        <VolumeTwo aria-hidden="true" />
        {{ $t("dashboard.overlays.audioMix.open") }}
      </Button>
      <PageCreateButton
        :label="$t('dashboard.overlays.new')"
        :disabled-reason="can('widget:write') ? undefined : $t('common.readOnly')"
        @create="creating = true"
      />
    </template>
    <template #bar>
      <PageTabs v-if="can('widget:read')" :tabs="tabs" />
    </template>
  </PageHeader>
  <PanelBeside>
    <ListDetail :show-detail="asked !== ''">
      <template #list>
        <ListPanel
          v-if="can('widget:read')"
          v-model="search"
          class="min-h-80 flex-1"
          :title="$t('dashboard.overlays.list.title')"
          :placeholder="$t('dashboard.overlays.search')"
          :description="count === undefined ? hint : `${count} · ${hint}`"
          :state="panelState"
          :empty-text="emptyText"
          :error-text="$t('dashboard.overlays.list.error')"
          @retry="widgets.refetch()"
        >
          <OverlayRow
            v-for="widget in visible"
            :key="widget.id"
            :widget="widget"
            :selected="widget.id === selectedId"
            @pick="pick(widget)"
          />
        </ListPanel>
      </template>
      <template #detail>
        <DetailEditorPane
          :open="asked !== ''"
          :missing="!selected || !can('widget:read')"
          :idle-title="$t('dashboard.overlays.detail.title')"
          :idle-text="$t('dashboard.overlays.detail.pick')"
          :missing-text="$t('dashboard.overlays.detail.notFound', { id: asked })"
          :back-label="$t('dashboard.overlays.back')"
          @back="pane.close()"
        >
          <OverlayDetail
            v-if="selected && can('widget:read')"
            :widget="selected"
            @deleted="pane.close()"
            @duplicated="open"
          />
        </DetailEditorPane>
      </template>
    </ListDetail>
    <template v-if="can('sounds:read')" #panel>
      <SidePanel v-model:open="audioOpen" :label="$t('dashboard.overlays.audioMix.title')">
        <AudioMixPanel flush />
      </SidePanel>
    </template>
  </PanelBeside>
  <NewOverlayDialog v-model:open="creating" @created="open" />
</template>
