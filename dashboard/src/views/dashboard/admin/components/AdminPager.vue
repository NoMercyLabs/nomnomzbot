<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The foot of a paged admin panel: where you are, and the way to the next and previous page.
import ChevronLeft from '~icons/nnz/chevron-left';
import ChevronRight from '~icons/nnz/chevron-right';

import { Button } from '@/components/ui/button';

defineProps<{ page: number; pages: number }>();

defineEmits<{ previous: []; next: [] }>();
</script>

<template>
  <span class="text-sm text-muted-foreground tabular-nums" role="status">
    {{ $t('dashboard.admin.pager.page', { page, pages }) }}
  </span>
  <div class="ml-auto flex items-center gap-2">
    <Button variant="outline" size="sm" :disabled="page <= 1" @click="$emit('previous')">
      <ChevronLeft aria-hidden="true" />
      {{ $t('dashboard.admin.pager.previous') }}
    </Button>
    <Button variant="outline" size="sm" :disabled="page >= pages" @click="$emit('next')">
      {{ $t('dashboard.admin.pager.next') }}
      <ChevronRight aria-hidden="true" />
    </Button>
  </div>
</template>
