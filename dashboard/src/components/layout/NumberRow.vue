<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A setting that holds a number: title and help on the left, a number input on the right. The default sits
// beside the title (hint); once the value differs from it, a reset button puts the default back. A refusal
// from the bot shows under the row and marks the input invalid. Radius 8 fits a RowGroup (12 - 4).
import { useId } from 'vue';

import ArrowCounterClockwise from '~icons/nnz/arrow-counter-clockwise';

import { Input } from '@/components/ui/input';

import IconAction from './IconAction.vue';
import RowText from './RowText.vue';

withDefaults(
  defineProps<{
    title: string;
    description?: string;
    hint?: string;
    defaultValue?: number;
    error?: string;
    step?: number;
  }>(),
  {
    description: undefined,
    hint: undefined,
    defaultValue: undefined,
    error: undefined,
    step: 1,
  },
);

const value = defineModel<number>({ default: 0 });
const titleId = useId();

function change(typed: string | number): void {
  value.value = Number(typed);
}
</script>

<template>
  <div data-slot="number-row" class="min-h-11 rounded-[8px] px-3 py-2">
    <div class="flex items-center gap-3">
      <span class="min-w-0 flex-1">
        <RowText :title="title" :title-id="titleId" :description="description" :hint="hint" />
      </span>
      <IconAction
        v-if="defaultValue !== undefined && value !== defaultValue"
        data-action="reset-field"
        :label="$t('common.resetField')"
        @click="value = defaultValue"
      >
        <ArrowCounterClockwise class="size-5" />
      </IconAction>
      <Input
        type="number"
        inputmode="decimal"
        class="w-28 flex-none text-end"
        :model-value="value"
        :step="step"
        :aria-labelledby="titleId"
        :aria-invalid="error === undefined ? undefined : 'true'"
        @update:model-value="change"
      />
    </div>
    <p v-if="error" data-row-error role="alert" class="mt-1 text-sm text-destructive">{{ error }}</p>
  </div>
</template>
