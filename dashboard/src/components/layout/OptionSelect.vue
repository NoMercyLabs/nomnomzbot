<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The one picker for a list of known choices: the design-system Select, or, when `searchable` is set (a long list
// such as every channel), a combobox that filters as the person types. No page builds a native select. Extra
// attributes (data-*, aria-label) land on the trigger, so a page and its test find the control by its own hook.
import ChevronDown from '~icons/nnz/chevron-down';
import Check from '~icons/nnz/check';
import { computed, ref } from 'vue';

import { Button } from '@/components/ui/button';
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

export interface Choice {
  value: string;
  label: string;
}

defineOptions({ inheritAttrs: false });

const props = withDefaults(
  defineProps<{
    options: Choice[];
    placeholder?: string;
    searchable?: boolean;
    searchPlaceholder?: string;
    emptyText?: string;
    disabled?: boolean;
  }>(),
  {
    placeholder: '',
    searchable: false,
    searchPlaceholder: '',
    emptyText: '',
    disabled: false,
  },
);

const model = defineModel<string>({ default: '' });

// Reka reserves the empty string for "nothing chosen", so a real choice whose value is empty ("no tier") travels
// under this stand-in.
const BLANK = '__blank';

const open = ref(false);

const shown = computed(() =>
  props.options.map((option) => ({
    ...option,
    key: option.value === ''
      ? BLANK
      : option.value,
  })));
const current = computed(() =>
  shown.value.find((option) =>
    option.value === model.value));
const selectValue = computed(() =>
  current.value?.key ?? '');

function pick(key: string): void {
  model.value = key === BLANK
    ? ''
    : key;
}

function pickFromSearch(key: string): void {
  open.value = false;
  pick(key);
}
</script>

<template>
  <Popover v-if="searchable" v-model:open="open">
    <PopoverTrigger as-child>
      <Button
        v-bind="$attrs"
        type="button"
        variant="outline"
        role="combobox"
        :aria-expanded="open"
        :disabled="disabled"
        class="group w-full justify-between font-normal"
      >
        <span class="truncate" :class="!current && 'text-muted-foreground'">
          {{ current?.label ?? placeholder }}
        </span>
        <ChevronDown
          class="text-muted-foreground transition-transform group-data-[state=open]:rotate-180 motion-reduce:transition-none"
          aria-hidden="true"
        />
      </Button>
    </PopoverTrigger>
    <PopoverContent align="start" class="w-(--reka-popover-trigger-width) min-w-64 p-0">
      <Command>
        <CommandInput :placeholder="searchPlaceholder" />
        <CommandList>
          <CommandEmpty>{{ emptyText }}</CommandEmpty>
          <CommandGroup>
            <CommandItem
              v-for="option in shown"
              :key="option.key"
              :data-option="option.key"
              :value="option.label"
              @select="pickFromSearch(option.key)"
            >
              <span class="min-w-0 flex-1 truncate">{{ option.label }}</span>
              <Check v-if="option.value === model" aria-hidden="true" />
            </CommandItem>
          </CommandGroup>
        </CommandList>
      </Command>
    </PopoverContent>
  </Popover>
  <Select
    v-else
    :model-value="selectValue"
    :disabled="disabled"
    @update:model-value="pick(String($event ?? ''))"
  >
    <SelectTrigger v-bind="$attrs" class="w-full">
      <SelectValue v-if="current">
        {{ current.label }}
      </SelectValue>
      <SelectValue v-else :placeholder="placeholder" />
    </SelectTrigger>
    <SelectContent>
      <SelectItem
        v-for="option in shown"
        :key="option.key"
        :data-option="option.key"
        :value="option.key"
      >
        {{ option.label }}
      </SelectItem>
    </SelectContent>
  </Select>
</template>
