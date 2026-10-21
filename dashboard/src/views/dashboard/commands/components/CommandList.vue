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
// channel command that repeats one is marked, so the owner can delete the copy.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  triggerWord,
  useBuiltins,
  useCommands,
  useToggleCommand,
  type CommandListItem,
} from '@/api/commands';
import { Switch } from '@/components/ui/switch';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import SearchField from '@/components/layout/SearchField.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useCan } from '@/composables/useCan';
import { usePanelState } from '@/composables/usePanelState';

export type CommandSort = 'uses' | 'name';

// The sort sits in the page control bar; the search sits in the panel header, so the list panel lines up with the detail panel.
const props = defineProps<{
  selected: string | null;
  sort: CommandSort;
}>();
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
const duplicates = computed<number>(
  () => all.value.filter((row): boolean => systemTwin(row) !== undefined).length,
);

const rows = computed<CommandListItem[]>(() =>
  all.value
    .filter((row): boolean => matches(row, needle.value))
    .sort((left: CommandListItem, right: CommandListItem): number =>
      props.sort === 'name'
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
        {{ $t("dashboard.commands.list.empty") }}
        <Button v-if="can('commands:write')" type="button" size="sm" @click="emit('create')">{{
          $t("dashboard.commands.list.makeFirst")
        }}</Button>
      </div>
      <template v-else>
        <p
          v-if="duplicates > 0"
          data-system-duplicates
          class="px-4 py-2 text-sm text-muted-foreground"
        >
          {{ $t("dashboard.commands.list.duplicates", { count: duplicates, total: all.length }) }}
        </p>
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
          <template v-if="!row.isEnabled || systemTwin(row)" #end>
            <StatusBadge
              v-if="systemTwin(row)"
              data-system-duplicate
              tone="off"
              :label="$t('dashboard.commands.list.repeats', { name: `!${systemTwin(row)}` })"
            />
            <StatusBadge v-if="!row.isEnabled" tone="off" :label="$t('dashboard.commands.list.off')" />
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
  </div>
</template>
