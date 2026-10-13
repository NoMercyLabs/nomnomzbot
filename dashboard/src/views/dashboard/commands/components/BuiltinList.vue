<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The commands the bot ships with. This slice only switches them on and off.
import { computed, type ComputedRef } from 'vue'

import { useBuiltins, useToggleBuiltin, type BuiltinCommand } from '@/api/commands'
import PanelCard from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import SettingRow from '@/components/layout/SettingRow.vue'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { usePanelState } from '@/composables/usePanelState'

const { channelId } = useCurrentChannel()
const builtins = useBuiltins(channelId)
const toggle = useToggleBuiltin(channelId)

const rows: ComputedRef<BuiltinCommand[]> = computed<BuiltinCommand[]>(() => builtins.data.value ?? [])
const state = usePanelState(builtins, (data: BuiltinCommand[]): boolean => data.length === 0)
</script>

<template>
  <div class="min-h-0 flex-1 px-6 pt-4 pb-6">
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
          :title="`!${row.name}`"
          :model-value="row.isEnabled"
          @update:model-value="toggle.mutate({ builtinKey: row.builtinKey, enabled: $event })"
        />
      </RowGroup>
    </PanelCard>
  </div>
</template>
