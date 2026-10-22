<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import Plus from '~icons/nnz/add';
import { useEventListener, useLocalStorage } from '@vueuse/core';
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
import PageActionFooter from '@/components/layout/PageActionFooter.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PageTabs from '@/components/layout/PageTabs.vue';
import type { PageTab } from '@/components/layout/pageTab';
import PanelCard from '@/components/layout/PanelCard.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useScreenFrom } from '@/composables/useLayout';
import { usePageView } from '@/composables/usePageView';
import { isTyping } from '@/composables/useShortcut';

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
      <Button v-if="canWrite && tablet"
              type="button"
              size="sm"
              @click="creating = true"
      >
        <Plus aria-hidden="true" />
        {{ $t("dashboard.commands.new") }}
      </Button>
    </template>
    <template #bar>
      <!-- The sort sits over the list it orders. The built-in list keeps the bot's own order: no sort there.
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
  <!-- The page's one primary action: below the tablet width it sits in the frame's sticky action footer. -->
  <PageActionFooter v-if="canWrite && !tablet && !open">
    <Button type="button" class="h-9! w-full" @click="creating = true">
      <Plus aria-hidden="true" />
      {{ $t("dashboard.commands.new") }}
    </Button>
  </PageActionFooter>
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
      <div v-if="row" class="flex flex-1 flex-col gap-2 md:min-h-0">
        <BackLink
          data-detail-back
          data-page-inset
          class="self-start md:hidden"
          :label="$t('dashboard.commands.back')"
          @click="select(undefined)"
        />
        <CommandEditor
          :key="row.name"
          :row="row"
          @remove="removeCommand"
          @duplicated="select"
          @close="select(undefined)"
        />
      </div>
      <div v-else-if="builtin && view === 'builtin'" class="flex flex-1 flex-col gap-2 md:min-h-0">
        <BackLink
          data-detail-back
          data-page-inset
          class="self-start md:hidden"
          :label="$t('dashboard.commands.back')"
          @click="select(undefined)"
        />
        <BuiltinEditor
          :key="builtin.builtinKey"
          data-builtin-detail
          :builtin="builtin"
          @close="select(undefined)"
        />
      </div>
      <PanelCard
        v-else
        state="empty"
        :title="$t('dashboard.commands.list.pickTitle')"
        :empty-text="$t('dashboard.commands.list.pick')"
      />
    </template>
  </ListDetail>
  <NewCommandDialog v-model:open="creating" @created="select" />
</template>
