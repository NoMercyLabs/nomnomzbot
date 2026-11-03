<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// What a test fire presented: the warnings first, then the chat line as chat, the speech, the overlay alerts and
// the pipeline steps that ran. A step of the economy category runs for real, so a line says so.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import type { EventResponseTestResult } from '@/api/eventResponses';
import { usePipelineCatalog } from '@/api/pipelines';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const props = defineProps<{ result: EventResponseTestResult }>();

const economyCategory = 'pipeline.category.economy';

const { t, te } = useI18n();
const { channelId } = useCurrentChannel();
const catalog = usePipelineCatalog(channelId);

const warningLines = computed<string[]>(() =>
  props.result.warnings.map((code): string =>
    te(`dashboard.eventResponses.test.warnings.${code}`)
      ? t(`dashboard.eventResponses.test.warnings.${code}`)
      : t('dashboard.eventResponses.test.warningOther', { code })));

const economySteps = computed<string[]>(() => {
  const economyTypes = new Set(
    (catalog.data.value?.actions ?? [])
      .filter((action): boolean => action.category.key === economyCategory)
      .map((action): string => action.type),
  );

  return props.result.pipelineSteps
    .map((step): string => step.actionType)
    .filter((type): boolean => economyTypes.has(type));
});

// A pipeline response does its chat inside its steps, so the lines its send_message steps sent are the chat line.
const chatLines = computed<string[]>(() => {
  if (props.result.chatLine)
    return [props.result.chatLine];

  return props.result.pipelineSteps
    .filter((step): boolean => step.actionType === 'send_message' && Boolean(step.output))
    .map((step): string => step.output ?? '');
});

function stepText(step: EventResponseTestResult['pipelineSteps'][number]): string {
  const outcome = step.succeeded
    ? t('dashboard.eventResponses.test.stepRan')
    : t('dashboard.eventResponses.test.stepFailed', { reason: step.errorMessage ?? '' });

  return `${step.actionType} - ${outcome}`;
}
</script>

<template>
  <div data-test-result class="grid gap-4">
    <p
      v-for="line in warningLines"
      :key="line"
      data-test-warning
      role="status"
      class="rounded-[8px] border border-border bg-muted px-3 py-2 text-sm font-medium"
    >
      {{ line }}
    </p>
    <p
      v-if="economySteps.length > 0"
      data-test-economy
      role="alert"
      class="rounded-[8px] border border-destructive px-3 py-2 text-sm font-medium text-destructive"
    >
      {{ $t('dashboard.eventResponses.test.economy', { steps: economySteps.join(', ') }) }}
    </p>

    <section class="grid gap-2">
      <h3 class="text-sm font-medium">{{ $t('dashboard.eventResponses.test.chat') }}</h3>
      <template v-if="chatLines.length > 0">
        <p
          v-for="(line, index) in chatLines"
          :key="index"
          data-test-chat
          class="w-fit max-w-full rounded-[8px] bg-muted px-3 py-2 text-sm wrap-break-word"
        >
          {{ line }}
        </p>
      </template>
      <p v-else class="text-sm text-muted-foreground">{{ $t('dashboard.eventResponses.test.chatNone') }}</p>
    </section>

    <section class="grid gap-2">
      <h3 class="text-sm font-medium">{{ $t('dashboard.eventResponses.test.tts') }}</h3>
      <ul v-if="result.speechLines.length > 0" class="grid gap-1 text-sm">
        <li v-for="(line, index) in result.speechLines" :key="index" class="wrap-break-word">
          <span data-test-tts>{{ line.text ?? $t('dashboard.eventResponses.test.ttsStep') }}</span>
          <span v-if="line.refusedReason" data-test-tts-refused class="block text-muted-foreground">
            {{ line.refusedReason }}
          </span>
        </li>
      </ul>
      <p v-else class="text-sm text-muted-foreground">{{ $t('dashboard.eventResponses.test.ttsNone') }}</p>
    </section>

    <section class="grid gap-2">
      <h3 class="text-sm font-medium">{{ $t('dashboard.eventResponses.test.alerts') }}</h3>
      <ul v-if="result.alerts.length > 0" class="grid gap-1 text-sm">
        <li v-for="(alert, index) in result.alerts" :key="index" data-test-alert class="wrap-break-word">{{ alert }}</li>
      </ul>
      <p v-else class="text-sm text-muted-foreground">{{ $t('dashboard.eventResponses.test.alertsNone') }}</p>
    </section>

    <section class="grid gap-2">
      <h3 class="text-sm font-medium">{{ $t('dashboard.eventResponses.test.steps') }}</h3>
      <ul v-if="result.pipelineSteps.length > 0" class="grid gap-1">
        <li v-for="step in result.pipelineSteps" :key="step.index" data-test-step>
          <StatusBadge :tone="step.succeeded ? 'ok' : 'bad'" :label="stepText(step)" />
        </li>
      </ul>
      <p v-else class="text-sm text-muted-foreground">{{ $t('dashboard.eventResponses.test.stepsNone') }}</p>
    </section>
  </div>
</template>
