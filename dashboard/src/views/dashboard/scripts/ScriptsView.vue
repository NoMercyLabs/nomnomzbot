<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The Scripts page: every code script of the channel as a list, tabs that filter it, and a slot for one script beside it.
// The script in view is the id in ?script=. Tab counts come from the loaded list: the bot has no count endpoint.
// A refusal of the list (custom code switched off, no right) shows the bot's own reason, not the empty text.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';

import { useCodeScripts, type CodeScriptSummary } from '@/api/codeScripts';
import DetailEditorPane from '@/components/layout/DetailEditorPane.vue';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import ListDetail from '@/components/layout/ListDetail.vue';
import ListPanel from '@/components/layout/ListPanel.vue';
import PageCreateButton from '@/components/layout/PageCreateButton.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useFilteredResourceList } from '@/composables/useFilteredResourceList';
import { errorText } from '@/lib/errors';
import { dashboardReturn } from '@/router/returnTarget';

import FailedRunDialog from './components/FailedRunDialog.vue';
import NewScriptDialog from './components/NewScriptDialog.vue';
import ScriptDetail from './components/ScriptDetail.vue';
import ScriptRow from './components/ScriptRow.vue';
import ScriptRunsPanel from './components/ScriptRunsPanel.vue';
import type { SessionRun } from './components/sessionRun';
import { useScriptPlanGate } from './components/useScriptPlanGate';

const FILTERS = {
  all: () => true,
  on: (script: CodeScriptSummary) => script.isEnabled,
  off: (script: CodeScriptSummary) => !script.isEnabled,
  failed: (script: CodeScriptSummary) => Boolean(script.lastRuntimeError),
} as const satisfies Record<string, (script: CodeScriptSummary) => boolean>;

const { t } = useI18n();
const { can } = useCan();
const route = useRoute();
const router = useRouter();

const scripts = useCodeScripts();
const planGate = useScriptPlanGate();

const loaded = computed(() => scripts.data.value);

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
} = useFilteredResourceList({
  query: scripts,
  items: () => loaded.value,
  filters: FILTERS,
  tabLabel: (tab) => t(`dashboard.scripts.tabs.${tab}`),
  searchText: (script) => [script.name, script.description ?? ''],
  paneKey: 'script',
});

const EMPTY_KEYS = {
  none: 'dashboard.scripts.list.empty',
  filtered: 'dashboard.scripts.list.emptyTab',
} as const;

const emptyText = computed<string>(() =>
  empty.value.kind === 'noMatch'
    ? t('dashboard.scripts.noMatch', { query: empty.value.query })
    : t(EMPTY_KEYS[empty.value.kind]));
const listError = computed<string>(
  () =>
    scripts.error.value
      ? errorText(scripts.error.value, t)
      : t('dashboard.scripts.list.error'),
);

const hint = computed<string>(() => t(`dashboard.scripts.hints.${view.value}`));
const count = computed<string | undefined>(() =>
  shown.value === undefined
    ? undefined
    : t('dashboard.scripts.count', { count: shown.value }),
);

// A page that sent the person here for one script (a pipeline step) names the way back in ?back=.
const backTo = computed(() =>
  dashboardReturn(router, route.query.back));

const newOpen = ref(false);

// The test runs of this visit, newest first. A failed run opens its dialog at once.
const runs = ref<SessionRun[]>([]);
const failedRun = ref<SessionRun | null>(null);
const runsOpen = ref(false);
let nextRunId: number = 1;

function ran(run: Omit<SessionRun, 'id'>): void {
  const entry: SessionRun = { ...run, id: nextRunId++ };

  runs.value = [entry, ...runs.value];

  if (!entry.result.success) {
    failedRun.value = entry;
  }
}

// A new or duplicated script opens in place; a cleared id (nothing was made) opens nothing.
function opened(id: string | null): void {
  if (id !== null) {
    pane.open(id);
  }
}
</script>

<template>
  <PageHeader
    :title="$t('dashboard.scripts.title')"
    :crumb="$t('dashboard.scripts.crumb')"
    :back="backTo"
    :back-label="$t('dashboard.scripts.backToPipeline')"
  >
    <template #actions>
      <Button v-if="runs.length > 0"
              variant="outline"
              size="sm"
              class="xl:hidden"
              @click="runsOpen = true"
      >
        {{ $t('dashboard.scripts.runs.button') }}
      </Button>
      <PageCreateButton
        :label="$t('dashboard.scripts.new.button')"
        :disabled-reason="can('code:script:author') ? planGate.reason.value : $t('common.readOnly')"
        @create="newOpen = true"
      />
    </template>
    <template #bar>
      <PageTabs :tabs="tabs" />
    </template>
  </PageHeader>
  <NewScriptDialog v-model:open="newOpen" @created="opened" />
  <InlineNotice v-if="planGate.blocked.value" tone="gated" data-plan-notice>
    {{ planGate.reason.value }}
  </InlineNotice>
  <ListDetail
    v-model:aside-open="runsOpen"
    :show-detail="asked !== ''"
    :aside-label="$t('dashboard.scripts.runs.title')"
  >
    <template #list>
      <ListPanel
        v-model="search"
        class="min-h-80 flex-1"
        :title="$t('dashboard.scripts.list.title')"
        :placeholder="$t('dashboard.scripts.search')"
        :description="count === undefined ? hint : `${count} · ${hint}`"
        :state="panelState"
        :empty-text="emptyText"
        :error-text="listError"
        @retry="scripts.refetch()"
      >
        <ScriptRow
          v-for="script in visible"
          :key="script.id"
          :script="script"
          :selected="script.id === selectedId"
          @pick="pane.open(script.id)"
        />
      </ListPanel>
    </template>
    <template #detail>
      <DetailEditorPane
        :open="asked !== ''"
        :idle-title="$t('dashboard.scripts.detail.title')"
        :idle-text="$t('dashboard.scripts.detail.pick')"
        :back-label="$t('dashboard.scripts.detail.back')"
        @back="pane.close"
      >
        <ScriptDetail
          :key="asked"
          :script-id="asked"
          :summary="loaded?.find((one: CodeScriptSummary) => one.id === asked)"
          @deleted="pane.close"
          @duplicated="opened"
          @ran="ran"
        />
      </DetailEditorPane>
    </template>
    <template v-if="runs.length > 0" #aside>
      <ScriptRunsPanel :runs="runs" @open="failedRun = $event" />
    </template>
  </ListDetail>
  <FailedRunDialog :run="failedRun" @close="failedRun = null" />
</template>
