<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import { useEventListener, useLocalStorage } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  triggerWord,
  useBuiltins,
  useCommands,
  useDeleteCommand,
  type BuiltinCommand,
  type CommandListItem,
} from '@/api/commands';
import DetailEditorPane from '@/components/layout/DetailEditorPane.vue';
import ListDetail from '@/components/layout/ListDetail.vue';
import PageCreateButton from '@/components/layout/PageCreateButton.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import type { PageTab } from '@/components/layout/pageTab';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useScreenFrom } from '@/composables/useLayout';
import { usePageView } from '@/composables/usePageView';
import { useQueryPane } from '@/composables/useQueryPane';
import { isTyping } from '@/composables/useShortcut';

import BuiltinEditor from './components/BuiltinEditor.vue';
import BuiltinList from './components/BuiltinList.vue';
import CommandEditor from './components/CommandEditor.vue';
import CommandList, { type CommandSort } from './components/CommandList.vue';
import NewCommandDialog from './components/NewCommandDialog.vue';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const tablet = useScreenFrom('tablet');
const commands = useCommands(channelId);
const remove = useDeleteCommand(channelId);
const builtins = useBuiltins(channelId);

// Editing is a write: a reader gets the lists only, and the Built-in tab (switches only) is hidden.
const canWrite = computed<boolean>(() => can('commands:write'));
// The channel's own commands and the bot's system commands never share one list (owner 2026-10-21).
const VIEWS = ['custom', 'builtin'] as const;

type CommandView = (typeof VIEWS)[number];
const allTabs = computed<PageTab<CommandView>[]>(() => [
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
const commandPane = useQueryPane('command', ['builtin']);
const selected = commandPane.id;
const row = computed<CommandListItem | undefined>(() =>
  (commands.data.value ?? []).find((item): boolean => item.name === selected.value),
);

// A built-in command opens by its name under ?builtin=, beside a custom one under ?command=.
const builtinPane = useQueryPane('builtin', ['command']);
const selectedBuiltin = builtinPane.id;
const builtin = computed<BuiltinCommand | undefined>(() =>
  (builtins.data.value ?? []).find((item): boolean => triggerWord(item) === selectedBuiltin.value),
);
const open = computed<boolean>(() => row.value !== undefined || builtin.value !== undefined);

function select(name: string | undefined): void {
  if (name === undefined) {
    commandPane.close();

    return;
  }

  commandPane.open(name);
}

function selectBuiltin(name: string): void {
  builtinPane.open(name);
}

function removeCommand(name: string): void {
  select(undefined);
  remove.mutate({ commandName: name });
}

// Escape closes the open command, as the close button and browser Back do. A key meant for a field, an
// open menu or a dialog stays there.
function closeOnEscape(event: KeyboardEvent): void {
  if (
    event.key !== 'Escape' ||
    event.defaultPrevented ||
    !open.value ||
    isTyping(event.target) ||
    document.querySelector('[data-dismissable-layer]:not([data-state="closed"])') !== null
  ) return;

  select(undefined);
}

useEventListener(document, 'keydown', closeOnEscape);

const creating = ref(false);
</script>

<template>
  <PageHeader
    :title="$t('dashboard.commands.title')"
    :crumb="$t('dashboard.commands.crumb')"
    :bar-hidden-on-phone="open"
  >
    <template #actions>
      <PageCreateButton
        :label="$t('dashboard.commands.new')"
        :disabled-reason="canWrite ? undefined : $t('common.readOnly')"
        @create="creating = true"
      />
    </template>
    <template #bar>
      <!-- The sort sits over the list it orders. The built-in list is always A to Z, so it has no sort control.
           On a phone the sort joins the search in the list's own control row. -->
      <SegmentedControl
        v-if="listed && tablet"
        v-model="sort"
        :label="$t('dashboard.commands.list.sort')"
        :options="sortOptions"
      />
      <PageTabs :tabs="tabs" />
    </template>
  </PageHeader>
  <ListDetail :show-detail="open">
    <template #list>
      <BuiltinList v-if="!listed" :selected="selectedBuiltin" @pick="selectBuiltin" />
      <CommandList
        v-else
        :selected="selected"
        v-model:sort="sort"
        :sort-options="sortOptions"
        @pick="select"
        @create="creating = true"
      />
    </template>
    <template v-if="canWrite" #detail>
      <DetailEditorPane
        :open="row !== undefined || (builtin !== undefined && view === 'builtin')"
        :idle-title="$t('dashboard.commands.list.pickTitle')"
        :idle-text="$t('dashboard.commands.list.pick')"
        :back-label="$t('dashboard.commands.back')"
        @back="select(undefined)"
      >
        <CommandEditor
          v-if="row"
          :key="row.name"
          :row="row"
          @remove="removeCommand"
          @duplicated="select"
          @close="select(undefined)"
        />
        <BuiltinEditor
          v-else-if="builtin"
          :key="builtin.builtinKey"
          data-builtin-detail
          :builtin="builtin"
          @close="select(undefined)"
        />
      </DetailEditorPane>
    </template>
  </ListDetail>
  <NewCommandDialog v-model:open="creating" @created="select" />
</template>
