<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Status is a dot plus words, never a tinted pill (C5). The words carry the meaning;
// the dot has the three mockup colors only (frame.css:148-150).
export type StatusTone = 'ok' | 'bad' | 'live' | 'off'

defineProps<{ tone: StatusTone; label: string }>()

const dotClass = {
  ok: 'bg-success',
  bad: 'bg-destructive',
  live: 'bg-destructive',
  off: 'bg-muted-foreground',
} as const satisfies Record<StatusTone, string>
</script>

<template>
  <span
    data-slot="status-badge"
    :data-tone="tone"
    class="inline-flex items-center gap-2 text-sm whitespace-nowrap"
  >
    <span aria-hidden="true" class="size-2 flex-none rounded-full" :class="dotClass[tone]" />
    {{ label }}
  </span>
</template>
