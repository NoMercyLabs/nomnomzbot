<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The commands: one row per command that matches the search, in the page's sort. The open command is the `selected` name.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  triggerWord,
  useBuiltins,
  useCommands,
  type BuiltinCommand,
  type CommandListItem,
} from '@/api/commands';
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { merge } from '@/lib/array';

export type CommandSort = 'uses' | 'name';

// The sort sits in the page control bar; the search sits in the panel header, so the list panel lines up with the detail panel.
// With `withBuiltins` (the All tab) the bot's own commands join the list, after the custom ones by use.
const props = withDefaults(
  defineProps<{
    selected: string | null;
    selectedBuiltin?: string | null;
    sort: CommandSort;
    withBuiltins?: boolean;
  }>(),
  { selectedBuiltin: null, withBuiltins: false },
);
const emit = defineEmits<{
  pick: [name: string];
  pickBuiltin: [name: string];
  create: [];
}>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const commands = useCommands(channelId);
const builtins = useBuiltins(channelId);

const search = ref('');

const panel = usePanelState(commands, (data: CommandListItem[]): boolean =>
  data.length === 0);
// The empty state holds a button, so the panel shows it as ready content.
const state = computed<PanelState>(() =>
  panel.value === 'empty'
    ? 'ready'
    : panel.value);
const none = computed<boolean>(() => panel.value === 'empty');

function matches(row: CommandListItem, needle: string): boolean {
  return [row.name, ...row.aliases].some((word: string): boolean =>
    word.toLowerCase().includes(needle),
  );
}

// One list row: a custom command, or a built-in one when the page shows all. A built-in has no use count.
type ListRow = {
  key: string;
  name: string;
  detail: string;
  isEnabled: boolean;
  uses: number;
  builtin: boolean;
};

const needle = computed<string>(() => search.value.trim().replace(/^!+/, '').toLowerCase());

const customRows = computed<ListRow[]>(() =>
  (commands.data.value ?? [])
    .filter((row: CommandListItem): boolean => matches(row, needle.value))
    .map((row: CommandListItem): ListRow => ({
      key: row.id,
      name: row.name,
      detail: detailOf(row),
      isEnabled: row.isEnabled,
      uses: Number(row.useCount),
      builtin: false,
    })));

const builtinRows = computed<ListRow[]>(() => {
  if (!props.withBuiltins)
    return [];

  return (builtins.data.value ?? [])
    .filter((row: BuiltinCommand): boolean => triggerWord(row).toLowerCase().includes(needle.value))
    .map((row: BuiltinCommand): ListRow => ({
      key: `builtin:${row.builtinKey}`,
      name: triggerWord(row),
      detail: t('dashboard.commands.list.kindBuiltin'),
      isEnabled: row.isEnabled,
      uses: -1,
      builtin: true,
    }));
});

const rows = computed<ListRow[]>(() =>
  merge(customRows.value, builtinRows.value).slice().sort((left: ListRow, right: ListRow): number =>
    props.sort === 'name'
      ? left.name.localeCompare(right.name)
      : right.uses - left.uses,
  ));

function isSelected(row: ListRow): boolean {
  return row.builtin
    ? row.name === props.selectedBuiltin
    : row.name === props.selected;
}

function pick(row: ListRow): void {
  if (row.builtin) {
    emit('pickBuiltin', row.name);

    return;
  }

  emit('pick', row.name);
}

// The row names what kind of command it is; the reply text itself lives in the editor (owner 2026-10-13).
function kindOf(row: CommandListItem): string {
  if (row.tier === 'pipeline')
    return t('dashboard.commands.list.kindPipeline');

  if (row.tier === 'code')
    return t('dashboard.commands.list.kindCode');

  if ((row.templateResponses?.length ?? 0) > 0)
    return t('dashboard.commands.list.kindRandom');

  return t('dashboard.commands.list.kindText');
}

function detailOf(row: CommandListItem): string {
  const uses = Number(row.useCount);

  return `${kindOf(row)} · ${t('dashboard.commands.list.uses', { count: uses }, uses)}`;
}
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col gap-3">
    <PanelCard
      :title="$t('dashboard.commands.list.title')"
      :state="state"
      :error-text="$t('dashboard.commands.list.error')"
      @retry="commands.refetch()"
    >
      <template #actions>
        <!-- Concentric with the panel corner: 8px from the top and the right, so a 20 - 8 = 12px radius. -->
        <SearchField
          v-model="search"
          class="-mr-2 w-52 rounded-[12px]"
          :placeholder="$t('dashboard.commands.list.search')"
        />
      </template>
      <div
        v-if="none"
        data-slot="panel-empty"
        class="flex h-full flex-col items-center justify-center gap-3 p-4 text-center text-sm text-muted-foreground"
      >
        {{ $t('dashboard.commands.list.empty') }}
        <Button type="button" size="sm" @click="emit('create')">{{
          $t('dashboard.commands.list.makeFirst')
        }}</Button>
      </div>
      <p v-else-if="rows.length === 0" class="p-4 text-center text-sm text-muted-foreground">
        {{ $t('dashboard.commands.list.noMatch') }}
      </p>
      <SummaryRow
        v-for="row in rows"
        v-else
        :key="row.key"
        pick
        :selected="isSelected(row)"
        :title="`!${row.name}`"
        :detail="row.detail"
        data-command-row
        :data-command-name="row.name"
        :data-command-builtin="row.builtin ? '' : undefined"
        @click="pick(row)"
      >
        <template v-if="!row.isEnabled" #end>
          <StatusBadge tone="off" :label="$t('dashboard.commands.list.off')" />
        </template>
      </SummaryRow>
    </PanelCard>
  </div>
</template>
