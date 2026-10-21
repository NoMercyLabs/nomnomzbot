<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channel's own commands: one row per command that matches the search, in the page's sort. The open
// command is the `selected` name. The bot's system commands live on their own tab, never in this list; a
// channel command that repeats one is marked, and one confirmed action moves every copy out to system.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  triggerWord,
  useBuiltins,
  useCommands,
  useDeleteCommand,
  useToggleCommand,
  type CommandListItem,
} from '@/api/commands';
import { Switch } from '@/components/ui/switch';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import SearchField from '@/components/layout/SearchField.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useCan } from '@/composables/useCan';
import { useScreenFrom } from '@/composables/useLayout';
import { usePanelState } from '@/composables/usePanelState';

export type CommandSort = 'uses' | 'name';

// From the tablet width the sort sits in the page control bar and the search in the panel header, so the list
// panel lines up with the detail panel. On a phone the panel has no header: the sort and the search share one row.
defineProps<{
  selected: string | null;
  sortOptions: readonly { value: CommandSort; label: string }[];
}>();

const sort = defineModel<CommandSort>('sort', { required: true });
const emit = defineEmits<{
  pick: [name: string];
  create: [];
}>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const commands = useCommands(channelId);
const builtins = useBuiltins(channelId);
const toggleCommand = useToggleCommand(channelId);

const tablet = useScreenFrom('tablet');
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
  return [row.name, ...row.aliases].some((word): boolean => word.toLowerCase().includes(needle));
}

const needle = computed<string>(() => search.value.trim().replace(/^!+/, '').toLowerCase());

// The system command words, so a channel command named like one (or with one as an alias) is found.
const systemWords = computed<Set<string>>(
  () =>
    new Set((builtins.data.value ?? []).map((row): string => triggerWord(row).toLowerCase())),
);

function systemTwin(row: CommandListItem): string | undefined {
  return [row.name, ...row.aliases].find((word): boolean =>
    systemWords.value.has(word.toLowerCase()));
}

const all = computed<CommandListItem[]>(() => commands.data.value ?? []);
const copies = computed<CommandListItem[]>(() =>
  all.value.filter((row): boolean => systemTwin(row) !== undefined));
const duplicates = computed<number>(() => copies.value.length);

// The system command already answers to the copy's word, so moving a copy to system removes the channel copy.
const removeCommand = useDeleteCommand(channelId);
const moveOpen = ref(false);
const moveNames = computed<string>(() =>
  copies.value.map((row): string => `!${row.name}`).join(', '));

async function moveCopies(): Promise<void> {
  const names = copies.value.map((row): string => row.name);

  moveOpen.value = false;

  for (const commandName of names) {
    await removeCommand.mutateAsync({ commandName });
  }
}

const rows = computed<CommandListItem[]>(() =>
  all.value
    .filter((row): boolean => matches(row, needle.value))
    .sort((left: CommandListItem, right: CommandListItem): number =>
      sort.value === 'name'
        ? left.name.localeCompare(right.name)
        : Number(right.useCount) - Number(left.useCount),
    ));

function toggle(commandName: string, isEnabled: boolean): void {
  toggleCommand.mutate({ commandName, isEnabled });
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
    <div v-if="!tablet" data-command-controls class="flex items-center gap-2 px-4">
      <SearchField
        v-model="search"
        class="min-w-0 flex-1"
        :placeholder="$t('dashboard.commands.list.search')"
      />
      <SegmentedControl v-model="sort" :label="$t('dashboard.commands.list.sort')" :options="sortOptions" />
    </div>
    <PanelCard
      :title="$t('dashboard.commands.list.title')"
      :headless="!tablet"
      :state="state"
      :error-text="$t('dashboard.commands.list.error')"
      @retry="commands.refetch()"
    >
      <template v-if="tablet" #actions>
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
        {{ $t("dashboard.commands.list.empty") }}
        <Button v-if="can('commands:write')" type="button" size="sm" @click="emit('create')">{{
          $t("dashboard.commands.list.makeFirst")
        }}</Button>
      </div>
      <template v-else>
        <div
          v-if="duplicates > 0"
          class="flex items-center gap-3 px-4 py-2"
        >
          <p data-system-duplicates class="flex-1 text-sm text-muted-foreground">
            {{ $t("dashboard.commands.list.duplicates", { count: duplicates, total: all.length }) }}
          </p>
          <Button
            v-if="can('commands:write')"
            type="button"
            size="sm"
            variant="outline"
            :disabled="removeCommand.isPending.value"
            @click="moveOpen = true"
          >
            {{ $t("dashboard.commands.list.moveCopies") }}
          </Button>
        </div>
        <p v-if="rows.length === 0" class="p-4 text-center text-sm text-muted-foreground">
          {{ $t("dashboard.commands.list.noMatch") }}
        </p>
        <SummaryRow
          v-for="row in rows"
          :key="row.id"
          pick
          :selected="row.name === selected"
          :title="`!${row.name}`"
          :detail="detailOf(row)"
          data-command-row
          :data-command-name="row.name"
          @click="emit('pick', row.name)"
        >
          <!-- From the tablet width the chips sit beside the switch; on a phone they sit under the name, so
               the switch column keeps one x position on every row. -->
          <template v-if="tablet && (!row.isEnabled || systemTwin(row))" #end>
            <StatusBadge
              v-if="systemTwin(row)"
              data-system-duplicate
              tone="off"
              :label="$t('dashboard.commands.list.repeats', { name: `!${systemTwin(row)}` })"
            />
            <StatusBadge v-if="!row.isEnabled" tone="off" :label="$t('dashboard.commands.list.off')" />
          </template>
          <template v-if="!tablet && (!row.isEnabled || systemTwin(row))" #meta>
            <span class="flex flex-wrap items-center gap-x-3 gap-y-1">
              <StatusBadge
                v-if="systemTwin(row)"
                data-system-duplicate
                tone="off"
                :label="$t('dashboard.commands.list.repeats', { name: `!${systemTwin(row)}` })"
              />
              <StatusBadge v-if="!row.isEnabled" tone="off" :label="$t('dashboard.commands.list.off')" />
            </span>
          </template>
          <template v-if="can('commands:write')" #trailing>
            <Switch
              :model-value="row.isEnabled"
              :aria-label="`!${row.name}`"
              @update:model-value="toggle(row.name, $event)"
            />
          </template>
        </SummaryRow>
      </template>
    </PanelCard>
    <ConfirmDialog
      v-model:open="moveOpen"
      :title="$t('dashboard.commands.list.moveTitle', { count: duplicates })"
      :description="$t('dashboard.commands.list.moveBody', { names: moveNames })"
      :action="$t('dashboard.commands.list.moveAction')"
      @confirm="moveCopies"
    />
  </div>
</template>
