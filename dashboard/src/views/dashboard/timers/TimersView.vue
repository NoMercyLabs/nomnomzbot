<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Triggers and Timers page: the channel's timers on the left, the open timer on the right.
import Plus from '~icons/nnz/add';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';

import { useTimers, type Timer } from '@/api/liveOps';
import { useDeleteTimer } from '@/api/timers';
import BackLink from '@/components/layout/BackLink.vue';
import ListDetail from '@/components/layout/ListDetail.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import type { PageTab } from '@/components/layout/pageTab';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePageView } from '@/composables/usePageView';
import { keysOf } from '@/lib/array';

import ChatTriggersPanel from './components/ChatTriggersPanel.vue';
import NewTimerDialog from './components/NewTimerDialog.vue';
import TimerEditor from './components/TimerEditor.vue';
import TimerList from './components/TimerList.vue';

const { t } = useI18n();
const route = useRoute();
const router = useRouter();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const timers = useTimers(channelId);
const remove = useDeleteTimer(channelId);

// The open timer lives in the address (?timer=), so a refresh or a link keeps it.
const selected = computed<string | null>(() =>
  typeof route.query.timer === 'string'
    ? route.query.timer
    : null,
);
const row = computed<Timer | undefined>(() =>
  (timers.data.value ?? []).find((item): boolean => item.id === selected.value),
);

function select(id: string | undefined): void {
  void router.push({ query: { ...route.query, timer: id } });
}

function removeTimer(item: Timer): void {
  select(undefined);
  remove.mutate({ timerId: item.id, name: item.name });
}

const creating = ref(false);
const creatingTrigger = ref(false);

// Each tab needs the read key of its own endpoint; the New button needs the write key of the open tab.
const tabKeys = {
  timers: { read: 'timers:read', write: 'timers:write' },
  triggers: { read: 'chattriggers:read', write: 'chattriggers:write' },
} as const;
const VIEWS = keysOf(tabKeys);

type TimersTab = (typeof VIEWS)[number];
const allTabs = computed<PageTab<TimersTab>[]>(() => [
  { value: 'timers', label: t('dashboard.timers.tabs.timers') },
  { value: 'triggers', label: t('dashboard.timers.tabs.triggers') },
]);

function canRead(tab: PageTab<TimersTab>): boolean {
  const keys = tabKeys[tab.value];

  return can(keys.read);
}

const tabs = computed(() => allTabs.value.filter(canRead));
const view = usePageView(VIEWS, 'view', () =>
  tabs.value.map((tab) => tab.value));
const canCreate = computed<boolean>(() =>
  can(tabKeys[view.value === 'triggers'
    ? 'triggers'
    : 'timers'].write));

function create(): void {
  if (view.value === 'triggers') {
    creatingTrigger.value = true;

    return;
  }

  creating.value = true;
}
</script>

<template>
  <PageHeader :title="$t('dashboard.timers.title')" :crumb="$t('dashboard.timers.crumb')">
    <template #actions>
      <Button v-if="canCreate"
              type="button"
              variant="outline"
              size="sm"
              @click="create"
      >
        <Plus aria-hidden="true" />
        {{ view === 'triggers' ? $t('dashboard.timers.triggers.new') : $t('dashboard.timers.new') }}
      </Button>
    </template>
    <template #bar>
      <PageTabs :tabs="tabs" />
    </template>
  </PageHeader>
  <ChatTriggersPanel v-if="view === 'triggers'" v-model:creating="creatingTrigger" />
  <ListDetail v-else :show-detail="row !== undefined">
    <template #list>
      <TimerList :selected="selected" @pick="select" @create="creating = true" />
    </template>
    <template v-if="can('timers:write')" #detail>
      <div v-if="row" class="flex flex-1 flex-col gap-2 md:min-h-0">
        <BackLink
          data-detail-back
          data-page-inset
          class="self-start md:hidden"
          :label="$t('dashboard.timers.back')"
          @click="select(undefined)"
        />
        <TimerEditor
          :key="row.id"
          :row="row"
          @remove="removeTimer"
          @duplicated="select($event ?? undefined)"
        />
      </div>
      <PanelCard
        v-else
        state="empty"
        :title="$t('dashboard.timers.list.pickTitle')"
        :empty-text="$t('dashboard.timers.list.pick')"
      />
    </template>
  </ListDetail>
  <NewTimerDialog v-model:open="creating" />
</template>
