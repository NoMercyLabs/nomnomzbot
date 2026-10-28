<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One script in the list: its name, when it last ran, whether that run worked, and its on/off switch.
// The switch sits beside the pick button, not inside it, so a press on it never opens the script.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useSetScriptEnabled, type CodeScriptSummary } from '@/api/codeScripts';
import { useCan } from '@/composables/useCan';
import { useFormattedTime } from '@/composables/useTime';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Switch } from '@/components/ui/switch';

const props = defineProps<{ script: CodeScriptSummary; selected: boolean }>();

defineEmits<{ pick: [] }>();

const { t } = useI18n();
const { can } = useCan();
const setEnabled = useSetScriptEnabled();

const ago = useFormattedTime(() => props.script.lastRanAt, 'relative', { live: true });
const detail = computed<string>(() =>
  props.script.lastRanAt
    ? t('dashboard.scripts.list.lastRun', { when: ago.value })
    : t('dashboard.scripts.list.neverRun'),
);

function toggle(isEnabled: boolean): void {
  setEnabled.mutate({ id: props.script.id, isEnabled });
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
          v-if="can('code:script:author')"
          class="mr-2"
          :model-value="script.isEnabled"
          :aria-label="$t('dashboard.scripts.list.switchLabel', { name: script.name })"
          @update:model-value="toggle(Boolean($event))"
        />
      </template>
    </SummaryRow>
  </div>
</template>
