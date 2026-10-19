<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import Plus from '~icons/nnz/add';
import { useLocalStorage } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';

import {
  triggerWord,
  useBuiltins,
  useCommands,
  useDeleteCommand,
  type BuiltinCommand,
  type CommandListItem,
} from '@/api/commands';
import BackLink from '@/components/layout/BackLink.vue';
import ListDetail from '@/components/layout/ListDetail.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import type { PageTab } from '@/components/layout/pageTab';
import PanelCard from '@/components/layout/PanelCard.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePageView } from '@/composables/usePageView';

import BuiltinEditor from './components/BuiltinEditor.vue';
import BuiltinList from './components/BuiltinList.vue';
import CommandEditor from './components/CommandEditor.vue';
import CommandList, { type CommandSort } from './components/CommandList.vue';
import NewCommandDialog from './components/NewCommandDialog.vue';

const { t } = useI18n();
const route = useRoute();
const router = useRouter();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const commands = useCommands(channelId);
const remove = useDeleteCommand(channelId);
const builtins = useBuiltins(channelId);

// Editing is a write: a reader gets the lists only, and the Built-in tab (switches only) is hidden.
const canWrite = computed<boolean>(() => can('commands:write'));
const VIEWS = ['all', 'custom', 'builtin'] as const;

type CommandView = (typeof VIEWS)[number];
const allTabs = computed<PageTab<CommandView>[]>(() => [
  { value: 'all', label: t('dashboard.commands.tabs.all') },
  { value: 'custom', label: t('dashboard.commands.tabs.custom') },
  { value: 'builtin', label: t('dashboard.commands.tabs.builtin') },
]);
const tabs = computed(() =>
  allTabs.value.filter((tab) => canWrite.value || tab.value !== 'builtin'),
);
const view = usePageView(VIEWS, 'view', () =>
  tabs.value.map((tab) => tab.value));
const listed = computed<boolean>(() => view.value !== 'builtin');

const sort = useLocalStorage<CommandSort>('nnz.commands.sort', 'uses');
const sortOptions = computed(
  () =>
    [
      { value: 'uses', label: t('dashboard.commands.list.sortUses') },
      { value: 'name', label: t('dashboard.commands.list.sortName') },
    ] as const satisfies { value: CommandSort; label: string }[],
);

// The open command lives in the address (?command=), so a refresh or a link keeps it.
const selected = computed<string | null>(() =>
  typeof route.query.command === 'string'
    ? route.query.command
    : null,
);
const row = computed<CommandListItem | undefined>(() =>
  (commands.data.value ?? []).find((item): boolean => item.name === selected.value),
);

// A built-in command opens by its name under ?builtin=, beside a custom one under ?command=.
const selectedBuiltin = computed<string | null>(() =>
  typeof route.query.builtin === 'string'
    ? route.query.builtin
    : null,
);
const builtin = computed<BuiltinCommand | undefined>(() =>
  (builtins.data.value ?? []).find((item): boolean => triggerWord(item) === selectedBuiltin.value),
);
const open = computed<boolean>(() => row.value !== undefined || builtin.value !== undefined);

function select(name: string | undefined): void {
  void router.push({
    query: {
      ...route.query,
      command: name,
      builtin: undefined,
    },
  });
}

function selectBuiltin(name: string): void {
  void router.push({
    query: {
      ...route.query,
      command: undefined,
      builtin: name,
    },
  });
}

function removeCommand(name: string): void {
  select(undefined);
  remove.mutate({ commandName: name });
}

const creating = ref(false);
</script>

<template>
  <PageHeader :title="$t('dashboard.commands.title')" :crumb="$t('dashboard.commands.crumb')">
    <template #actions>
      <Button v-if="canWrite"
              type="button"
              variant="outline"
              size="sm"
              @click="creating = true"
      >
        <Plus aria-hidden="true" />
        {{ $t("dashboard.commands.new") }}
      </Button>
    </template>
    <template #bar>
      <!-- The sort sits over the list it orders. It stays in place on the Built-in tab, shut, so switching
           tabs never moves the tabs. -->
      <SegmentedControl
        v-model="sort"
        :disabled="!listed"
        :title="listed ? undefined : $t('dashboard.commands.list.sortOff')"
        :label="$t('dashboard.commands.list.sort')"
        :options="sortOptions"
      />
      <PageTabs :tabs="tabs" />
    </template>
  </PageHeader>
  <ListDetail v-if="listed" :show-detail="open">
    <template #list>
      <CommandList
        :selected="selected"
        :selected-builtin="selectedBuiltin"
        :sort="sort"
        :with-builtins="view === 'all'"
        @pick="select"
        @pick-builtin="selectBuiltin"
        @create="creating = true"
      />
    </template>
    <template v-if="canWrite" #detail>
      <div v-if="row" class="flex flex-1 flex-col gap-2 md:min-h-0">
        <BackLink
          data-detail-back
          data-page-inset
          class="self-start md:hidden"
          :label="$t('dashboard.commands.back')"
          @click="select(undefined)"
        />
        <CommandEditor :key="row.name" :row="row" @remove="removeCommand" />
      </div>
      <BuiltinEditor
        v-else-if="builtin && view === 'all'"
        :key="builtin.builtinKey"
        data-builtin-detail
        :builtin="builtin"
      />
      <PanelCard
        v-else
        state="empty"
        :title="$t('dashboard.commands.list.pickTitle')"
        :empty-text="$t('dashboard.commands.list.pick')"
      />
    </template>
  </ListDetail>
  <BuiltinList v-else />
  <NewCommandDialog v-model:open="creating" @created="select" />
</template>
