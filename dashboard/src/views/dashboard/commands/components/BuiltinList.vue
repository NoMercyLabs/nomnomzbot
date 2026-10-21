<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The commands the bot ships with. A row opens the command's editor; the switch beside it turns the
// command on or off; a system command that is always on has no switch. The row shows who may use it, its cooldowns and whether the channel changed them.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { triggerWord, useBuiltins, useToggleBuiltin, type BuiltinCommand } from '@/api/commands';
import GatedSwitch from '@/components/layout/GatedSwitch.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

defineProps<{ selected: string | null }>();
const emit = defineEmits<{ pick: [name: string] }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const builtins = useBuiltins(channelId);
const toggle = useToggleBuiltin(channelId);

const search = ref('');
const needle = computed<string>(() => search.value.trim().replace(/^!+/, '').toLowerCase());

const rows = computed<BuiltinCommand[]>(() =>
  (builtins.data.value ?? []).filter((row): boolean =>
    triggerWord(row).toLowerCase().includes(needle.value),
  ),
);

type Settings = {
  role: string;
  cooldown: number;
  userCooldown: number;
};

const settingsLine = ({ role, cooldown, userCooldown }: Settings): string =>
  t('dashboard.commands.builtin.summary', {
    role: t(`dashboard.commands.builtin.roles.${role}`),
    cooldown,
    userCooldown,
  });

const defaultsOf = (row: BuiltinCommand) =>
  ({
    role: row.defaultMinPermissionLevel,
    cooldown: Number(row.defaultCooldownSeconds),
    userCooldown: Number(row.defaultUserCooldownSeconds),
  }) as const satisfies Settings;

const effectiveOf = (row: BuiltinCommand) =>
  ({
    role: row.minPermissionLevelOverride ?? row.defaultMinPermissionLevel,
    cooldown: Number(row.cooldownSecondsOverride ?? row.defaultCooldownSeconds),
    userCooldown: Number(row.userCooldownSecondsOverride ?? row.defaultUserCooldownSeconds),
  }) as const satisfies Settings;

const isChanged = (row: BuiltinCommand): boolean =>
  row.minPermissionLevelOverride != null ||
  row.cooldownSecondsOverride != null ||
  row.userCooldownSecondsOverride != null;

function descriptionOf(row: BuiltinCommand): string {
  if (row.isReserved)
    return t('dashboard.commands.builtin.reserved');

  const settings = settingsLine(effectiveOf(row));

  return row.isAlwaysOn
    ? t('dashboard.commands.builtin.alwaysOn', { settings })
    : settings;
}

const hintOf = (row: BuiltinCommand): string =>
  isChanged(row)
    ? t('dashboard.commands.builtin.overrides', { value: settingsLine(defaultsOf(row)) })
    : t('dashboard.commands.builtin.usingDefault');

const state = usePanelState(builtins, (data: BuiltinCommand[]): boolean =>
  data.length === 0);
</script>

<template>
  <PanelCard
    :title="$t('dashboard.commands.builtin.title')"
    :state="state"
    :empty-text="$t('dashboard.commands.builtin.empty')"
    :error-text="$t('dashboard.commands.builtin.error')"
    @retry="builtins.refetch()"
  >
    <template #actions>
      <!-- Concentric with the panel corner, as in the command list: a 12px radius. -->
      <SearchField
        v-model="search"
        class="-mr-2 w-52 rounded-[12px]"
        :placeholder="$t('dashboard.commands.list.search')"
      />
    </template>
    <p v-if="rows.length === 0" class="p-4 text-center text-sm text-muted-foreground">
      {{ $t("dashboard.commands.list.noMatch") }}
    </p>
    <SummaryRow
      v-for="row in rows"
      v-else
      :key="row.builtinKey"
      pick
      chevron
      :selected="triggerWord(row) === selected"
      :title="`!${triggerWord(row)}`"
      :detail="descriptionOf(row)"
      :meta="hintOf(row)"
      data-builtin-row
      @click="emit('pick', triggerWord(row))"
    >
      <template v-if="!row.isAlwaysOn" #trailing>
        <GatedSwitch
          action="commands:write"
          :model-value="row.isEnabled"
          :aria-label="`!${triggerWord(row)}`"
          @update:model-value="toggle.mutate({ builtinKey: row.builtinKey, enabled: $event })"
        />
      </template>
    </SummaryRow>
  </PanelCard>
</template>
