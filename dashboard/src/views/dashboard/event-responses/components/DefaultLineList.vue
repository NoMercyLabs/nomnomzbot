<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
