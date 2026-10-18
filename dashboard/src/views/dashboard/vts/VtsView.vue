<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The VTube Studio page: link it, then load models, fire hotkeys and set expressions.
// Dots: green linked, orange needs the approval, red failed, gray off.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useVtsConnection, useVtsInventory } from '@/api/vts';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs, { type PageTab } from '@/components/layout/PageTabs.vue';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePageView } from '@/composables/usePageView';
import { usePanelState } from '@/composables/usePanelState';

import VtsConnectionPanel from './components/VtsConnectionPanel.vue';
import VtsExpressionsPanel from './components/VtsExpressionsPanel.vue';
import VtsHotkeysPanel from './components/VtsHotkeysPanel.vue';
import { vtsHealth, vtsReady } from './vtsHealth';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();

const VIEWS = ['connection', 'hotkeys', 'expressions'] as const satisfies readonly string[];
const view = usePageView(VIEWS);
const tabs = computed<PageTab[]>(() =>
  VIEWS.map((value) => ({ value, label: t(`dashboard.vts.tabs.${value}`) })),
);

const connection = useVtsConnection(channelId);
const ready = computed<boolean>(() => vtsReady(connection.data.value));
const inventory = useVtsInventory(channelId, ready);

const connectionPanel = usePanelState(connection);
const inventoryPanel = usePanelState(inventory);
const health = computed(() => vtsHealth(connection.data.value));

function retry(): void {
  void connection.refetch();
  void inventory.refetch();
}
</script>

<template>
  <PageHeader :title="$t('dashboard.vts.title')" :crumb="$t('dashboard.vts.crumb')">
    <template #status>
      <span
        v-if="can('vts:config:read')"
        data-vts-overall
        class="inline-flex items-center gap-2 text-sm text-muted-foreground"
      >
        <span class="dot" :class="health.dot" aria-hidden="true" />
        {{ $t(`dashboard.vts.status.${health.reason}`) }}
      </span>
    </template>
    <template #bar><PageTabs v-if="can('vts:config:read')" :tabs="tabs" /></template>
  </PageHeader>
  <PageBody>
    <div v-if="can('vts:config:read')" data-page-inset class="grid gap-4 @container">
      <VtsHotkeysPanel
        v-if="view === 'hotkeys'"
        :inventory="inventory.data.value"
        :panel-state="inventoryPanel"
        :offline="!ready"
        @retry="retry"
      />
      <VtsExpressionsPanel
        v-else-if="view === 'expressions'"
        :inventory="inventory.data.value"
        :panel-state="inventoryPanel"
        :offline="!ready"
        @retry="retry"
      />
      <VtsConnectionPanel
        v-else
        :connection="connection.data.value"
        :panel-state="connectionPanel"
        @retry="retry"
      />
    </div>
  </PageBody>
</template>
