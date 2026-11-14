<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The first-run wizard. The bot lists the steps and says which are required; this page draws one at a time.
// One primary action per step: the step's own action until it is done, then Next.
import { useQuery } from '@tanstack/vue-query';
import { computed, ref } from 'vue';
import { useRouter } from 'vue-router';

import { setupWizardQuery, useCompleteSetup, type SetupStep } from '@/api/setup';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { Spinner } from '@/components/ui/spinner';

import PublicTaskShell from '../components/PublicTaskShell.vue';
import WizardActions from '../components/WizardActions.vue';
import PublicWait from '../components/PublicWait.vue';
import EventFeedHealth from './components/EventFeedHealth.vue';
import { rendererFor } from './components/setupSteps';
import StepsFrame from './components/StepsFrame.vue';

const router = useRouter();
const wizard = useQuery(setupWizardQuery);
const complete = useCompleteSetup();
const index = ref(0);

const steps = computed<SetupStep[]>(() => wizard.data.value?.steps ?? []);
const onFinish = computed(() => index.value >= steps.value.length);
const step = computed<SetupStep | null>(() => steps.value[index.value] ?? null);
// A required step must be done before Next; an optional one can be skipped.
const canAdvance = computed(
  () => step.value !== null && (step.value.complete || !step.value.required),
);
const skips = computed(
  () => step.value !== null && !step.value.complete && !step.value.required,
);

async function finish(): Promise<void> {
  await complete.mutateAsync();
  await router.replace('/app');
}
</script>

<template>
  <PublicTaskShell
    :brand="$t('site.setup.brand')"
    width="wizard"
    :align="wizard.isSuccess.value ? 'start' : 'center'"
  >
    <template v-if="!wizard.isSuccess.value">
      <h1 class="sr-only">{{ $t('site.setup.brand') }}</h1>
      <PublicWait v-if="wizard.isPending.value">{{ $t('site.setup.loading') }}</PublicWait>
      <InlineNotice v-else tone="error">
        {{ $t('site.setup.unreachable') }}
        <template #action>
          <Button variant="outline" @click="wizard.refetch()">{{ $t('site.setup.retry') }}</Button>
        </template>
      </InlineNotice>
    </template>

    <StepsFrame
      v-if="wizard.isSuccess.value"
      :steps="steps"
      :finish-title="$t('site.setup.finish.title')"
      :current="index"
    />

    <PanelCard
      v-if="wizard.isSuccess.value"
      :title="onFinish ? $t('site.setup.finish.title') : (step?.title ?? '')"
      :description="onFinish ? $t('site.setup.finish.description') : step?.description"
      heading="h1"
      tier="lead"
      pad
      class="w-full"
    >
      <div class="flex flex-col gap-4">
        <template v-if="step && !onFinish">
          <component :is="rendererFor(step.key)" :key="step.key" :step="step" />
        </template>

        <template v-else>
          <ul class="flex flex-col gap-2">
            <li v-for="s in steps" :key="s.key" class="flex items-center justify-between gap-3 text-sm">
              <span>{{ s.title }}</span>
              <StatusBadge
                :tone="s.complete ? 'ok' : 'off'"
                :label="s.complete ? $t('site.setup.done') : $t('site.setup.notSet')"
              />
            </li>
          </ul>
          <EventFeedHealth />
          <InlineNotice v-if="complete.isError.value" tone="error" compact>
            {{ $t('site.setup.finish.failed') }}
          </InlineNotice>
        </template>
      </div>

      <template #footer>
        <WizardActions>
          <template #back>
            <Button variant="ghost" :disabled="index === 0" @click="index -= 1">
              {{ $t('site.setup.back') }}
            </Button>
          </template>
          <template #next>
            <Button v-if="onFinish" :disabled="complete.isPending.value" @click="finish">
              <Spinner v-if="complete.isPending.value" />
              {{ $t('site.setup.finish.action') }}
            </Button>
            <Button
              v-else
              :variant="step?.complete ? 'default' : 'outline'"
              :disabled="!canAdvance"
              @click="index += 1"
            >
              {{ skips ? $t('site.setup.skip') : $t('site.setup.next') }}
            </Button>
          </template>
        </WizardActions>
      </template>
    </PanelCard>
  </PublicTaskShell>
</template>
