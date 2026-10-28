<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One closed choice (a role, a rung of the ladder, a capability). The page names every option; no number shows.
import { computed } from 'vue';

import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

import { groupOptions } from '../optionGroups';

// An option may name its group (its type); a long list then reads as labeled sections.
export type Option = {
  value: string;
  label: string;
  disabled?: boolean;
  group?: string;
};

defineOptions({ inheritAttrs: false });

const props = defineProps<{
  modelValue?: string;
  options: readonly Option[];
  label: string;
  placeholder?: string;
  disabled?: boolean;
}>();

const groups = computed(() => groupOptions(props.options));

defineEmits<{ 'update:modelValue': [value: string] }>();
</script>

<template>
  <Select
    :model-value="modelValue"
    :disabled="disabled"
    @update:model-value="$emit('update:modelValue', String($event))"
  >
    <SelectTrigger v-bind="$attrs" :aria-label="label" class="w-44">
      <SelectValue :placeholder="placeholder" />
    </SelectTrigger>
    <SelectContent>
      <SelectGroup v-for="group in groups" :key="group.label ?? ''">
        <SelectLabel v-if="group.label !== undefined">{{ group.label }}</SelectLabel>
        <SelectItem
          v-for="option in group.options"
          :key="option.value"
          :value="option.value"
          :disabled="option.disabled"
        >
          {{ option.label }}
        </SelectItem>
      </SelectGroup>
    </SelectContent>
  </Select>
</template>
