<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The four inline flows and the countdown.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import Countdown from '@/components/layout/Countdown.vue';
import InlineFlow from '@/components/layout/InlineFlow.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';

const { t } = useI18n();

const countdownTarget = ref(Date.now() + 15_000);
const countdownZeros = ref(0);

function restartCountdown(): void {
  countdownTarget.value = Date.now() + 15_000;
}

function pause(): Promise<void> {
  return new Promise((resolve: () => void) => setTimeout(resolve, 900));
}
</script>

<template>
  <section data-catalog="flows" class="grid gap-4">
    <PanelCard :title="t('dashboard.catalog.flow.title')" pad>
      <div class="flex flex-col items-start gap-3">
        <InlineFlow
          kind="confirm"
          harm
          :label="t('dashboard.catalog.flow.confirm.label')"
          :ask="t('dashboard.catalog.flow.confirm.ask')"
          :yes="t('dashboard.catalog.flow.confirm.yes')"
          :result="t('dashboard.catalog.flow.confirm.result')"
          :action="pause"
          :revert="pause"
        >
          <Button variant="outline">{{ t('dashboard.catalog.flow.confirm.open') }}</Button>
        </InlineFlow>
        <InlineFlow
          kind="apply"
          :label="t('dashboard.catalog.flow.apply.label')"
          :result="t('dashboard.catalog.flow.apply.result')"
          :action="pause"
          :revert="pause"
        >
          <Button variant="outline">{{ t('dashboard.catalog.flow.apply.open') }}</Button>
        </InlineFlow>
        <InlineFlow
          kind="run"
          :label="t('dashboard.catalog.flow.run.label')"
          :text="t('dashboard.catalog.flow.run.text')"
          :result="t('dashboard.catalog.flow.run.result')"
          :action="pause"
        >
          <Button variant="outline">{{ t('dashboard.catalog.flow.run.open') }}</Button>
        </InlineFlow>
        <InlineFlow
          kind="note"
          :label="t('dashboard.catalog.flow.note.label')"
          :text="t('dashboard.catalog.flow.note.text')"
        >
          <Button variant="outline">{{ t('dashboard.catalog.flow.note.open') }}</Button>
        </InlineFlow>
      </div>
    </PanelCard>

    <PanelCard :title="t('dashboard.catalog.countdown.title')" pad>
      <div class="flex items-center gap-4">
        <b class="text-2xl font-semibold">
          <Countdown
            :target="countdownTarget"
            :label="t('dashboard.catalog.countdown.label')"
            @zero="countdownZeros += 1"
          />
        </b>
        <Button variant="outline" @click="restartCountdown">
          {{ t('dashboard.catalog.countdown.restart') }}
        </Button>
        <span class="text-sm text-muted-foreground">
          {{ t('dashboard.catalog.countdown.count', { count: countdownZeros }) }}
        </span>
      </div>
    </PanelCard>
  </section>
</template>
