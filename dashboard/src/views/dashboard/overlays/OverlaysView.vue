<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Overlays page: every overlay of the channel as a list, tabs that filter it, and one overlay beside it.
// The overlay in view is the id in ?id=, so a link opens it; with no id the first row of the list shows.
// Tab counts come from the loaded list: the bot has no count endpoint.
// The audio levels are channel-wide, not one overlay's, so they open from the header in their own drawer.
import VolumeTwo from '~icons/nnz/volume-two';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';

import { useRequiredOverlays, useWidgets, withoutRequired, type Widget } from '@/api/widgets';
import ListDetail from '@/components/layout/ListDetail.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import type { PageTab } from '@/components/layout/pageTab';
import PanelCard from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import SidePanel from '@/components/layout/SidePanel.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useNarrowScreen } from '@/composables/useLayout';
import { usePageView } from '@/composables/usePageView';
import { keysOf } from '@/lib/array';
import { usePanelState } from '@/composables/usePanelState';

import AudioMixPanel from './components/AudioMixPanel.vue';
import OverlayDetail from './components/OverlayDetail.vue';
import OverlayRow from './components/OverlayRow.vue';

const FILTERS = {
  all: () => true,
  on: (widget: Widget) => widget.isEnabled,
  off: (widget: Widget) => !widget.isEnabled,
  problem: (widget: Widget) => Boolean(widget.lastRuntimeError),
} as const satisfies Record<string, (widget: Widget) => boolean>;
const TAB_VALUES = keysOf(FILTERS);

type OverlayTab = (typeof TAB_VALUES)[number];

const { t } = useI18n();
const route = useRoute();
const router = useRouter();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const widgets = useWidgets(channelId);
const view = usePageView(TAB_VALUES);
const search = ref('');
const audioOpen = ref(false);

const required = useRequiredOverlays(channelId);

// The required overlays are shown only on the OBS page; this list is the user's own choices.
const loaded = computed(() =>
  widgets.data.value
    ? withoutRequired(widgets.data.value, required.data.value)
    : undefined,
);

function countOf(tab: OverlayTab): number | undefined {
  return loaded.value?.filter(FILTERS[tab]).length;
}

const tabs = computed(() =>
  TAB_VALUES.map((value) => ({
    value,
    label: t(`dashboard.overlays.tabs.${value}`),
    count: countOf(value),
  }) as const satisfies PageTab<OverlayTab>),
);

const visible = computed(() => {
  const typed = search.value.trim().toLowerCase();

  return loaded.value
    ?.filter(FILTERS[view.value])
    .filter((widget: Widget) => typed === '' || widget.name.toLowerCase().includes(typed));
});

const listState = usePanelState(widgets, () => false);
const panelState = computed(() => {
  if (listState.value !== 'ready')
    return listState.value;

  return visible.value?.length === 0
    ? 'empty'
    : 'ready';
});

const emptyText = computed<string>(() => {
  if (search.value.trim() !== '')
    return t('dashboard.overlays.noMatch', { query: search.value.trim() });

  return t(
    loaded.value?.length === 0
      ? 'dashboard.overlays.list.empty'
      : 'dashboard.overlays.list.emptyTab',
  );
});

const hint = computed<string>(() => t(`dashboard.overlays.hints.${view.value}`));
const count = computed<string | undefined>(() =>
  panelState.value === 'ready'
    ? t('dashboard.overlays.count', { count: visible.value?.length ?? 0 })
    : undefined,
);

const asked = computed<string>(() =>
  typeof route.query.id === 'string'
    ? route.query.id
    : '');
// An id in the address looks in every overlay, so a switched-off one stays open on the On tab.
const selected = computed<Widget | null>(() => {
  if (asked.value !== '')
    return loaded.value?.find((widget: Widget) => widget.id === asked.value) ?? null;

  return visible.value?.[0] ?? null;
});
const phone = useNarrowScreen();
// On a phone the list shows one screen or the other, so no row stays highlighted while no detail is open.
const selectedId = computed<string>(() =>
  phone.value && asked.value === ''
    ? ''
    : (selected.value?.id ?? ''),
);

function pick(widget: Widget): void {
  void router.push({ query: { ...route.query, id: widget.id } });
}

function back(): void {
  const { id, ...rest } = route.query;

  void id;
  void router.push({ query: rest });
}
</script>

<template>
  <PageHeader :title="$t('dashboard.overlays.title')" :crumb="$t('dashboard.overlays.crumb')">
    <template v-if="can('sounds:read')" #actions>
      <Button variant="outline" @click="audioOpen = true">
        <VolumeTwo aria-hidden="true" />
        {{ $t("dashboard.overlays.audioMix.open") }}
      </Button>
      <SidePanel
        v-model:open="audioOpen"
        drawer-only
        :label="$t('dashboard.overlays.audioMix.title')"
      >
        <AudioMixPanel flush />
      </SidePanel>
    </template>
    <template #bar>
      <PageTabs v-if="can('widget:read')" :tabs="tabs" />
    </template>
  </PageHeader>
  <ListDetail :show-detail="asked !== ''">
    <template #list>
      <PanelCard
        v-if="can('widget:read')"
        class="min-h-80 flex-1"
        :title="$t('dashboard.overlays.list.title')"
        :description="count === undefined ? hint : `${count} · ${hint}`"
        :state="panelState"
        :empty-text="emptyText"
        :error-text="$t('dashboard.overlays.list.error')"
        @retry="widgets.refetch()"
      >
        <template #actions>
          <!-- Concentric with the panel corner: 8px from the top and the right, so a 20 - 8 = 12px radius. -->
          <SearchField
            v-model="search"
            class="-mr-2 w-52 rounded-[12px]"
            :placeholder="$t('dashboard.overlays.search')"
          />
        </template>
        <OverlayRow
          v-for="widget in visible"
          :key="widget.id"
          :widget="widget"
          :selected="widget.id === selectedId"
          @pick="pick(widget)"
        />
      </PanelCard>
    </template>
    <template #detail>
      <OverlayDetail v-if="selected && can('widget:read')" :widget="selected" @back="back" />
      <PanelCard
        v-else
        class="flex-1"
        :title="$t('dashboard.overlays.detail.title')"
        state="empty"
        :empty-text="
          asked === ''
            ? $t('dashboard.overlays.detail.pick')
            : $t('dashboard.overlays.detail.notFound', { id: asked })
        "
      />
    </template>
  </ListDetail>
</template>
