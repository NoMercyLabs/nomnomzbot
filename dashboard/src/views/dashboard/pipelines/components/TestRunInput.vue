<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// What a test run starts with: names with values the steps read as {name}, as a real start would fill them.
import { computed } from 'vue';

import { Button } from '@/components/ui/button';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';

import ChevronDown from '~icons/nnz/chevron-down';

import MapField from './MapField.vue';

const input = defineModel<Record<string, string>>({ required: true });

const count = computed(() => Object.keys(input.value).length);

function change(value: Record<string, string> | undefined): void {
  input.value = value ?? {};
}
</script>

<template>
  <Popover>
    <PopoverTrigger as-child>
      <Button data-run-input
              type="button"
              variant="ghost"
              size="sm"
              class="group"
      >
        {{ $t('dashboard.pipelines.workbench.run.input', { count }, count) }}
        <ChevronDown
          class="transition-transform group-data-[state=open]:rotate-180 motion-reduce:transition-none"
          aria-hidden="true"
        />
      </Button>
    </PopoverTrigger>
    <PopoverContent align="end" class="flex w-80 flex-col gap-3">
      <p class="m-0 text-sm text-muted-foreground">
        {{ $t('dashboard.pipelines.workbench.run.inputHelp') }}
      </p>
      <MapField id="test-run-input" :value="input" @change="change" />
    </PopoverContent>
  </Popover>
</template>
