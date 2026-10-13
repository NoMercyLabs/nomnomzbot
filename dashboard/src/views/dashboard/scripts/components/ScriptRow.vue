<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One script in the list: its name, when it last ran, whether that run worked, and its on/off switch.
// The switch sits beside the pick button, not inside it, so a press on it never opens the script.
import { useTimeAgoIntl } from '@vueuse/core'
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'

import { useSetScriptEnabled, type CodeScriptSummary } from '@/api/codeScripts'
import StatusBadge from '@/components/layout/StatusBadge.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import { Switch } from '@/components/ui/switch'

const props = defineProps<{ script: CodeScriptSummary; selected: boolean }>()

defineEmits<{ pick: [] }>()

const { t, locale } = useI18n()
const setEnabled = useSetScriptEnabled()

const ago = useTimeAgoIntl(() => props.script.lastRanAt ?? new Date(0), {
  locale: locale.value,
})
const detail: ComputedRef<string> = computed<string>(() =>
  props.script.lastRanAt
    ? t('dashboard.scripts.list.lastRun', { when: ago.value })
    : t('dashboard.scripts.list.neverRun'),
)

function toggle(isEnabled: boolean): void {
  setEnabled.mutate({ id: props.script.id, isEnabled })
}
</script>

<template>
  <div data-script-row class="px-1 py-0.5">
    <SummaryRow
      pick
      :selected="selected"
      :aria-pressed="selected"
      :title="script.name"
      :detail="detail"
      @click="$emit('pick')"
    >
      <template #end>
        <StatusBadge
          v-if="script.lastRuntimeError"
          tone="bad"
          :label="$t('dashboard.scripts.list.failed')"
        />
        <StatusBadge
          v-else-if="script.lastRanAt"
          tone="ok"
          :label="$t('dashboard.scripts.list.worked')"
        />
      </template>
      <template #trailing>
        <Switch
          class="mr-2"
          :model-value="script.isEnabled"
          :aria-label="$t('dashboard.scripts.list.switchLabel', { name: script.name })"
          @update:model-value="toggle(Boolean($event))"
        />
      </template>
    </SummaryRow>
  </div>
</template>
