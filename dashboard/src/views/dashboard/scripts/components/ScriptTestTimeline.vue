<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// What one test run did, in order: step number, what kind of step it was, and its text on one three-column grid.
import { useI18n } from 'vue-i18n';

import type { TestRunResult } from '@/composables/useEditorBridge';

import { powerLabel } from './powerLabel';

defineProps<{ steps: TestRunResult['timeline'] }>();

const { t, te } = useI18n();

// A kind the dashboard has no word for shows as the bot sent it.
function kindLabel(kind: string): string {
  const key = `dashboard.scripts.test.kinds.${kind}`;

  return te(key)
    ? t(key)
    : kind;
}

// The bot writes an effect step as "effect.key: preview". The key reads as words; the preview stays as sent.
function stepText(kind: string, text: string): string {
  const split = text.indexOf(': ');

  if (kind !== 'effect' || split < 1)
    return text;

  return `${powerLabel(text.slice(0, split), t, te)}${text.slice(split)}`;
}
</script>

<template>
  <ol data-slot="script-test-timeline" class="grid gap-2">
    <li
      v-for="step in steps"
      :key="step.seq"
      class="grid grid-cols-[1.5rem_5.5rem_minmax(0,1fr)] items-baseline gap-x-3 max-sm:grid-cols-[1.5rem_minmax(0,1fr)]"
    >
      <span class="text-muted-foreground tabular-nums">{{ step.seq }}</span>
      <span class="font-medium text-muted-foreground">{{ kindLabel(step.kind) }}</span>
      <span class="wrap-break-word max-sm:col-start-2">{{ stepText(step.kind, step.text) }}</span>
    </li>
  </ol>
</template>
