<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A color field: the browser's own color picker as a swatch, beside a hex text field. Both edit one value.
import { computed } from 'vue';

import { Input } from '@/components/ui/input';

defineOptions({ inheritAttrs: false });

const props = defineProps<{
  disabled?: boolean;
  placeholder?: string;
  label: string;
}>();
const model = defineModel<string>({ required: true });

const hexPattern = /^#[0-9a-f]{6}$/i;

// The native picker only takes #rrggbb, so anything else shows as black until the text is a full hex.
const swatch = computed(() =>
  hexPattern.test(model.value)
    ? model.value
    : '#000000');

function pick(event: Event): void {
  model.value = (event.target as HTMLInputElement).value.toUpperCase();
}
</script>

<template>
  <div class="flex items-center gap-2" data-color-picker>
    <input
      type="color"
      data-color-swatch
      class="size-9 flex-none cursor-pointer rounded-md border border-input bg-transparent p-1 disabled:cursor-not-allowed disabled:opacity-50"
      :value="swatch"
      :disabled="props.disabled"
      :aria-label="props.label"
      @input="pick"
    >
    <Input
      v-bind="$attrs"
      v-model="model"
      autocomplete="off"
      :placeholder="props.placeholder"
      :disabled="props.disabled"
      :aria-label="props.label"
    />
  </div>
</template>
