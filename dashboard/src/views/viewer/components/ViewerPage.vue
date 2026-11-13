<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// One viewer page inside ViewerLayout: the title as the page's h1, a status and an action beside it, then the
// body. The layout owns the max width, so this adds none. state picks what the body shows: a skeleton while the
// page reads, an error line with a retry, an empty text, or the page itself. The footer slot shows in every
// state (a group that does not depend on the main read). It carries no management-shell part: no channel switch
// and no live-management navigation.
import EmptyState from '@/components/layout/EmptyState.vue';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import type { PanelState } from '@/composables/usePanelState';

withDefaults(
  defineProps<{
    title: string;
    state?: PanelState;
    errorText?: string;
    retryLabel?: string;
    emptyText?: string;
  }>(),
  {
    state: 'ready',
    errorText: undefined,
    retryLabel: undefined,
    emptyText: undefined,
  },
);

defineEmits<{ retry: [] }>();
</script>

<template>
  <div data-slot="viewer-page" class="flex flex-col gap-6 px-6">
    <header class="flex min-h-9 flex-wrap items-center justify-between gap-x-3 gap-y-2">
      <h1 class="min-w-0 text-xl font-semibold">{{ title }}</h1>
      <div class="flex items-center gap-2">
        <span v-if="$slots.status" data-slot="viewer-page-status"><slot name="status" /></span>
        <span v-if="$slots.actions" data-slot="viewer-page-actions"><slot name="actions" /></span>
      </div>
    </header>

    <InlineNotice
      v-if="state === 'error'"
      tone="error"
      compact
      data-slot="viewer-page-error"
      class="flex flex-col items-start gap-3"
    >
      {{ errorText ?? $t('common.panel.error') }}
      <Button variant="outline" size="sm" @click="$emit('retry')">
        {{ retryLabel ?? $t('common.panel.retry') }}
      </Button>
    </InlineNotice>
    <div v-else-if="state === 'loading'" data-slot="viewer-page-loading" aria-busy="true">
      <slot name="loading">
        <div class="grid gap-3">
          <Skeleton v-for="line in 3" :key="line" class="h-14 rounded-lg" />
        </div>
      </slot>
    </div>
    <slot v-else-if="state === 'empty' && $slots.empty" name="empty" />
    <EmptyState
      v-else-if="state === 'empty'"
      :icon="false"
      :description="emptyText ?? $t('common.panel.empty')"
    />
    <slot v-else />

    <slot name="footer" />
  </div>
</template>
