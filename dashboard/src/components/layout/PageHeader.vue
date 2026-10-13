<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The fixed page header (C2): a 64px title row, then an optional 48px row for the one tab row of the page.
// actions holds the one primary action; meta is quiet text beside it (for example "Updated 2 min ago").
defineProps<{ title: string; crumb?: string }>()
</script>

<template>
  <header class="flex-none">
    <div class="flex h-16 items-center gap-3 border-b border-border px-6">
      <span
        v-if="crumb"
        data-slot="page-crumb"
        class="text-muted-foreground after:ml-3 after:opacity-50 after:content-['/']"
      >
        {{ crumb }}
      </span>
      <h1 class="min-w-0 truncate text-xl font-semibold tracking-tight">{{ title }}</h1>
      <slot name="status" />
      <div class="ml-auto flex items-center gap-3">
        <span v-if="$slots.meta" class="text-muted-foreground"><slot name="meta" /></span>
        <slot name="actions" />
      </div>
    </div>
    <div v-if="$slots.tabs" class="flex h-12 items-center border-b border-border px-6">
      <slot name="tabs" />
    </div>
  </header>
</template>
