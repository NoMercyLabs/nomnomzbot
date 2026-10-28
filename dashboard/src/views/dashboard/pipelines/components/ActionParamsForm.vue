<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The settings of one action that runs on its own (a binding target), as the same fields the pipeline editor
// gives a step. The fields come from the bot's catalog; an emptied field leaves the settings.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { PipelineActionDescriptor } from '@/api/pipelines';
import { FieldGroup } from '@/components/ui/field';
import { useLocalizedText } from '@/composables/useLocalizedText';
import type { ActionParams } from '@/lib/bindingTarget';
import type { JsonValue } from '@/lib/json';

import { choiceContext, type FieldRow, settingRows, settingsAfter } from './stepFields';
import StepField from './StepField.vue';

const props = defineProps<{
  actionType: string;
  descriptor: PipelineActionDescriptor | undefined;
}>();

const params = defineModel<ActionParams>('params', { required: true });

const { t, te } = useI18n();
const text = useLocalizedText();

const rows = computed(() =>
  settingRows(props.actionType, params.value, props.descriptor, {
    t,
    te,
    text,
  }));

const context = computed(() =>
  choiceContext(rows.value));

function change(row: FieldRow, value: JsonValue | undefined): void {
  const next = { ...params.value, ...settingsAfter(rows.value, row, value) };

  for (const name of Object.keys(next)) {
    if (next[name] === undefined) {
      delete next[name];
    }
  }

  params.value = next;
}
</script>

<template>
  <FieldGroup v-if="rows.length > 0" data-action-params class="gap-4">
    <StepField
      v-for="row in rows"
      :key="`${actionType}:${row.name}`"
      :row="row"
      :context="context"
      @change="change(row, $event)"
    />
  </FieldGroup>
</template>
