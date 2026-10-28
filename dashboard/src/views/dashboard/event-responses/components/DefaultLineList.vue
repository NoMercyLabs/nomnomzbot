<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The default lines the bot picks from, one row each. Edit on a row hands that line over as the starting text.
import Edit from '~icons/nnz/edit';

import { Button } from '@/components/ui/button';

defineProps<{ lines: readonly string[] }>();
defineEmits<{ pick: [line: string] }>();
</script>

<template>
  <ul class="divide-y divide-border rounded-lg border border-border">
    <li
      v-for="line in lines"
      :key="line"
      data-reply-line
      class="flex items-center gap-3 py-1 ps-3 pe-1"
    >
      <span data-reply-text class="min-w-0 flex-1 text-sm">{{ line }}</span>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        :aria-label="$t('dashboard.eventResponses.editor.editLine')"
        @click="$emit('pick', line)"
      >
        <Edit class="size-5" aria-hidden="true" />
      </Button>
    </li>
  </ul>
</template>
