<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The commands the bot ships with: each row switches one on or off and shows who may use it, its
// cooldowns and whether the channel changed them from the default.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { triggerWord, useBuiltins, useToggleBuiltin, type BuiltinCommand } from '@/api/commands';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SettingRow from '@/components/layout/SettingRow.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const builtins = useBuiltins(channelId);
const toggle = useToggleBuiltin(channelId);

const rows = computed<BuiltinCommand[]>(() => builtins.data.value ?? []);

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

const descriptionOf = (row: BuiltinCommand): string =>
  row.isReserved
    ? t('dashboard.commands.builtin.reserved')
    : settingsLine(effectiveOf(row));

const hintOf = (row: BuiltinCommand): string =>
  isChanged(row)
    ? t('dashboard.commands.builtin.overrides', { value: settingsLine(defaultsOf(row)) })
    : t('dashboard.commands.builtin.usingDefault');

const state = usePanelState(builtins, (data: BuiltinCommand[]): boolean =>
  data.length === 0);
</script>

<template>
  <div data-page-body class="min-h-0 flex-1 px-6 pt-4 pb-6">
    <PanelCard
      :title="$t('dashboard.commands.builtin.title')"
      :state="state"
      :empty-text="$t('dashboard.commands.builtin.empty')"
      :error-text="$t('dashboard.commands.builtin.error')"
      @retry="builtins.refetch()"
    >
      <RowGroup>
        <SettingRow
          v-for="row in rows"
          :key="row.builtinKey"
          :title="`!${triggerWord(row)}`"
          :description="descriptionOf(row)"
          :hint="hintOf(row)"
          :disabled="row.isReserved"
          :model-value="row.isEnabled"
          @update:model-value="toggle.mutate({ builtinKey: row.builtinKey, enabled: $event })"
        />
      </RowGroup>
    </PanelCard>
  </div>
</template>
