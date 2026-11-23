<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot. You may use, change and share it for free,
   for any noncommercial purpose, under the PolyForm Noncommercial License
   1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
   See LICENSE for details, and TRADEMARKS.md for the name and logo.

   SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// The line under one override field of the command form: what the catalog default is, and the way back to it.
// The way back keeps its place when there is nothing to go back from, so the line never changes height.
import { Button } from '@/components/ui/button';

defineProps<{
  // What the line says: the default, or that the field overrides it.
  hint: string;
  // The field this line belongs to, named in the button's label.
  field: string;
  // Whether the payload sets the field, so the way back shows.
  overridden: boolean;
}>();

const emit = defineEmits<{ back: [] }>();
</script>

<template>
  <div class="flex min-h-9 items-center justify-between gap-2">
    <span class="text-sm text-muted-foreground">{{ hint }}</span>
    <Button
      type="button"
      variant="ghost"
      :class="overridden ? '' : 'invisible'"
      :aria-label="$t('dashboard.commands.builtin.backTo', { field })"
      @click="emit('back')"
    >
      {{ $t("dashboard.commands.builtin.back") }}
    </Button>
  </div>
</template>
