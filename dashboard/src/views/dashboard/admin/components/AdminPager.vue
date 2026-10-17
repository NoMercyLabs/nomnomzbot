<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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
