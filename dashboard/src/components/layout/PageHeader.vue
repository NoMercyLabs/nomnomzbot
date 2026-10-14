<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The fixed page header (C2): a 64px title row, then an optional 48px control bar for the page's tabs,
// filters and page-wide controls (the chat page's Display and Viewer), never inside a card header.
// actions holds the one primary action; meta is quiet text beside it (for example "Updated 2 min ago").
// The frame's controls follow the page's actions, so the page has one header row.
import { inject, type Component } from 'vue';

import { frameActionsKey } from './frameActions';

defineProps<{ title: string; crumb?: string }>();

// Typed slots: a page that fills a slot name this header does not have fails the type check.
defineSlots<{
  status?: () => unknown;
  meta?: () => unknown;
  actions?: () => unknown;
  bar?: () => unknown;
}>();

const frameActions: Component | null = inject(frameActionsKey, null);
</script>

<template>
  <!-- A narrow pane drops the quiet text first (meta, then the crumb); past that the actions wrap to their own line. -->
  <header class="@container flex-none">
    <div
      class="flex min-h-16 flex-wrap items-center gap-x-3 gap-y-2 border-b border-border px-6 py-3 @2xl:flex-nowrap @2xl:py-0"
    >
      <span
        v-if="crumb"
        data-slot="page-crumb"
        class="hidden text-muted-foreground after:ml-3 after:opacity-50 after:content-['/'] @xl:inline"
      >
        {{ crumb }}
      </span>
      <h1 class="min-w-0 truncate text-xl font-semibold tracking-tight">{{ title }}</h1>
      <slot name="status" />
      <div class="ml-auto flex shrink-0 items-center gap-3">
        <span v-if="$slots.meta"
              class="hidden text-muted-foreground @4xl:inline"
        ><slot name="meta" /></span>
        <slot name="actions" />
        <component :is="frameActions" v-if="frameActions" />
      </div>
    </div>
    <div
      v-if="$slots.bar"
      data-slot="page-bar"
      class="flex h-12 items-center gap-3 border-b border-border px-6"
    >
      <slot name="bar" />
    </div>
  </header>
</template>
