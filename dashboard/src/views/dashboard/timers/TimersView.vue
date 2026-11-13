<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The Triggers and Timers page: the channel's timers on the left, the open timer on the right.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useTimers, type Timer } from '@/api/liveOps';
import { useDeleteTimer } from '@/api/timers';
import DetailEditorPane from '@/components/layout/DetailEditorPane.vue';
import ListDetail from '@/components/layout/ListDetail.vue';
import PageCreateButton from '@/components/layout/PageCreateButton.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useQueryPane } from '@/composables/useQueryPane';
import { useTabbedPage, type TabSpec } from '@/composables/useTabbedPage';

import ChatTriggersPanel from './components/ChatTriggersPanel.vue';
import NewTimerDialog from './components/NewTimerDialog.vue';
import TimerEditor from './components/TimerEditor.vue';
import TimerList from './components/TimerList.vue';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const timers = useTimers(channelId);
const remove = useDeleteTimer(channelId);

// The open timer lives in the address (?timer=), so a refresh or a link keeps it.
const pane = useQueryPane('timer');
const selected = pane.id;
const row = computed<Timer | undefined>(() =>
  (timers.data.value ?? []).find((item): boolean => item.id === selected.value),
);

function removeTimer(item: Timer): void {
  pane.close();
  remove.mutate({ timerId: item.id, name: item.name });
}

const creating = ref(false);
const creatingTrigger = ref(false);

const VIEWS = ['timers', 'triggers'] as const;

type TimersTab = (typeof VIEWS)[number];

// Each tab needs the read key of its own endpoint; the New button needs the write key of the open tab.
const specs = (): readonly TabSpec<TimersTab>[] => [
  {
    value: 'timers',
    label: t('dashboard.timers.tabs.timers'),
    read: 'timers:read',
    action: {
      label: t('dashboard.timers.new'),
      write: 'timers:write',
      run: (): void => {
        creating.value = true;
      },
    },
  },
  {
    value: 'triggers',
    label: t('dashboard.timers.tabs.triggers'),
    read: 'chattriggers:read',
    action: {
      label: t('dashboard.timers.triggers.new'),
      write: 'chattriggers:write',
      run: (): void => {
        creatingTrigger.value = true;
      },
    },
  },
];

const { tabs, view, current, action, canAct } = useTabbedPage(VIEWS, specs);

// The New button stays in the header for a person who cannot create, locked with the reason.
const createLabel = computed<string>(() => current.value?.action?.label ?? '');

function create(): void {
  action.value?.run();
}
</script>

<template>
  <PageHeader :title="$t('dashboard.timers.title')" :crumb="$t('dashboard.timers.crumb')">
    <template #actions>
      <PageCreateButton
        :label="createLabel"
        :disabled-reason="canAct ? undefined : $t('common.readOnly')"
        @create="create"
      />
    </template>
    <template #bar>
      <PageTabs :tabs="tabs" />
    </template>
  </PageHeader>
  <ChatTriggersPanel v-if="view === 'triggers'" v-model:creating="creatingTrigger" />
  <ListDetail v-else :show-detail="row !== undefined">
    <template #list>
      <TimerList :selected="selected" @pick="pane.open" @create="creating = true" />
    </template>
    <template v-if="can('timers:write')" #detail>
      <DetailEditorPane
        :open="row !== undefined"
        :idle-title="$t('dashboard.timers.list.pickTitle')"
        :idle-text="$t('dashboard.timers.list.pick')"
        :back-label="$t('dashboard.timers.back')"
        @back="pane.close"
      >
        <TimerEditor
          v-if="row"
          :key="row.id"
          :row="row"
          @remove="removeTimer"
          @duplicated="pane.select"
        />
      </DetailEditorPane>
    </template>
  </ListDetail>
  <NewTimerDialog v-model:open="creating" />
</template>
