<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// What one test run did, in order: step number, what kind of step it was, and its text on one three-column grid.
import { useI18n } from 'vue-i18n';

import type { TestRunResult } from '@/composables/useEditorBridge';

defineProps<{ steps: TestRunResult['timeline'] }>();

const { t, te } = useI18n();

// A kind the dashboard has no word for shows as the bot sent it.
function kindLabel(kind: string): string {
  const key = `dashboard.scripts.test.kinds.${kind}`;

  return te(key)
    ? t(key)
    : kind;
}
</script>

<template>
  <ol data-slot="script-test-timeline" class="grid gap-1">
    <li
      v-for="step in steps"
      :key="step.seq"
      class="grid grid-cols-[1.5rem_5rem_minmax(0,1fr)] items-baseline gap-x-2 max-sm:grid-cols-[1.5rem_minmax(0,1fr)]"
    >
      <span class="text-muted-foreground tabular-nums">{{ step.seq }}</span>
      <span class="text-xs text-muted-foreground">{{ kindLabel(step.kind) }}</span>
      <span class="wrap-break-word max-sm:col-start-2">{{ step.text }}</span>
    </li>
  </ol>
</template>
