<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// What a live run did for real, above the steps: the actions that ran, then every warning as its own line
// (output nobody could see, such as an alert with no overlay open). The step trace stays on the cards.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { PipelineTestRun } from '@/api/pipelines';

import { actionTitle } from './stepWords';

const props = defineProps<{ run: PipelineTestRun }>();

const { t, te } = useI18n();

const ranForReal = computed(() =>
  (props.run.realSteps ?? []).map((type) =>
    actionTitle(type, { t, te })));
</script>

<template>
  <section
    data-live-result
    class="grid gap-2 rounded-lg border bg-muted px-4 py-3 text-sm"
  >
    <p v-if="ranForReal.length > 0" data-live-real-steps class="m-0">
      {{ $t('dashboard.pipelines.workbench.live.ran', { steps: ranForReal.join(', ') }) }}
    </p>
    <p v-else data-live-real-steps class="m-0">
      {{ $t('dashboard.pipelines.workbench.live.ranNone') }}
    </p>
    <p
      v-for="warning in run.warnings ?? []"
      :key="warning"
      data-live-warning
      role="status"
      class="m-0 text-destructive"
    >
      {{ warning }}
    </p>
  </section>
</template>
