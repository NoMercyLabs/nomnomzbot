<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The one inline notice: page or mutation feedback that sits in the flow (a plan gate, a closed queue,
// a failed run, a stored draft). A failure of a whole panel stays in PanelCard state="error".
// tone picks the border and text from the existing tokens; no tone is filled with the accent.
// compact is the plain one-line form with no border (error compact = the old destructive line).
// The action slot holds the one primary action of the notice; siblings stay outline.
// Attributes (data-* hooks, class) fall through to the root element.
import { computed } from 'vue';

type NoticeTone = 'info' | 'warning' | 'error' | 'success' | 'gated';

const TONE_BOX = {
  info: 'border bg-muted',
  warning: 'border border-warning/50 bg-muted',
  error: 'border border-destructive/50 text-destructive',
  success: 'border border-success/50 bg-muted',
  gated: 'border bg-muted/50 text-muted-foreground',
} as const satisfies Record<NoticeTone, string>;

const TONE_LINE = {
  info: 'text-muted-foreground',
  warning: 'text-foreground',
  error: 'text-destructive',
  success: 'text-foreground',
  gated: 'text-muted-foreground',
} as const satisfies Record<NoticeTone, string>;

const props = withDefaults(
  defineProps<{
    tone: NoticeTone;
    title?: string;
    compact?: boolean;
    role?: 'alert' | 'status';
  }>(),
  {
    title: undefined,
    compact: false,
    role: undefined,
  },
);

const resolvedRole = computed(() => props.role ?? (props.tone === 'error'
  ? 'alert'
  : 'status'));

const rootClass = computed(() =>
  props.compact
    ? `m-0 text-sm ${TONE_LINE[props.tone]}`
    : `m-0 flex flex-wrap items-center justify-between gap-3 rounded-lg px-4 py-3 text-sm ${TONE_BOX[props.tone]}`);
</script>

<template>
  <div :role="resolvedRole" :data-tone="tone" :class="rootClass">
    <div class="min-w-0 flex-1">
      <p v-if="title" data-notice-title class="m-0 font-medium">
        {{ title }}
      </p>
      <slot />
    </div>
    <span v-if="$slots.action" data-notice-action class="flex shrink-0 items-center gap-2">
      <slot name="action" />
    </span>
  </div>
</template>
