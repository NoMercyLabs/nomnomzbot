<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A color field as one input group: the browser's own color picker as a swatch at the left inside the
// hex text field, with one border and one focus ring. Both edit one value.
import { computed } from 'vue';

import { InputGroup, InputGroupAddon, InputGroupInput } from '@/components/ui/input-group';

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
  <InputGroup data-color-picker>
    <InputGroupAddon>
      <span
        data-color-chip
        class="relative size-5 flex-none overflow-hidden rounded-sm border border-input"
        :class="{ 'opacity-50': props.disabled }"
        :style="{ backgroundColor: swatch }"
      >
        <input
          type="color"
          data-color-swatch
          class="absolute inset-0 size-full cursor-pointer opacity-0 disabled:cursor-not-allowed"
          :value="swatch"
          :disabled="props.disabled"
          :aria-label="props.label"
          @input="pick"
        >
      </span>
    </InputGroupAddon>
    <InputGroupInput
      v-bind="$attrs"
      v-model="model"
      autocomplete="off"
      :placeholder="props.placeholder"
      :disabled="props.disabled"
      :aria-label="props.label"
    />
  </InputGroup>
</template>
