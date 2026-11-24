<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import Cross from '~icons/nnz/cross';

import { Button } from '@/components/ui/button';
import { DialogClose, DialogDescription, DialogTitle } from '@/components/ui/dialog';

defineProps<{
  title: string;
  description: string;
}>();
</script>

<template>
  <div class="flex flex-none items-start justify-between gap-4 px-6 pt-6 pb-4">
    <div class="grid gap-1.5">
      <DialogTitle class="text-lg font-semibold">{{ title }}</DialogTitle>
      <DialogDescription class="text-sm text-muted-foreground">
        {{ description }}
      </DialogDescription>
    </div>
    <DialogClose as-child>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        :aria-label="$t('common.close')"
      >
        <Cross class="size-5" aria-hidden="true" />
      </Button>
    </DialogClose>
  </div>
</template>
