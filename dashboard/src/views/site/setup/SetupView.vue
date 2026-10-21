<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The first-run wizard. The bot lists the steps and says which are required; this page draws one at a time.
// One primary action per step: the step's own action until it is done, then Next.
import { useQuery } from '@tanstack/vue-query';
import { computed, ref } from 'vue';
import { useRouter } from 'vue-router';

import { hasCredentialForm, setupWizardQuery, useCompleteSetup, type SetupStep } from '@/api/setup';
import AppMark from '@/components/layout/AppMark.vue';
import LanguagePicker from '@/components/layout/LanguagePicker.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';

import BotAccountStep from './components/BotAccountStep.vue';
import CredentialsStep from './components/CredentialsStep.vue';
import EventFeedHealth from './components/EventFeedHealth.vue';
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
  <main
    :class="cn(
      'relative flex min-h-svh flex-col items-center gap-6 p-6 pt-16',
      wizard.isSuccess.value ? 'justify-start' : 'justify-center',
    )"
  >
    <div class="absolute top-4 right-4"><LanguagePicker /></div>
    <h1 class="flex items-center gap-2 text-base font-semibold">
      <AppMark />
      {{ $t('site.setup.brand') }}
    </h1>

    <p v-if="wizard.isPending.value" class="text-sm text-muted-foreground" role="status">
      {{ $t('site.setup.loading') }}
    </p>
    <div v-else-if="wizard.isError.value" class="flex flex-col items-center gap-3" role="alert">
      <p class="text-sm">{{ $t('site.setup.unreachable') }}</p>
      <Button variant="outline" @click="wizard.refetch()">{{ $t('site.setup.retry') }}</Button>
    </div>

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
      tier="lead"
      pad
      class="w-full max-w-xl"
    >
      <div class="flex flex-col gap-4">
        <template v-if="step && !onFinish">
          <CredentialsStep v-if="hasCredentialForm(step.key)" :key="step.key" :step="step" />
          <BotAccountStep v-else :key="step.key" :step="step" />
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
          <p v-if="complete.isError.value" class="text-sm text-destructive" role="alert">
            {{ $t('site.setup.finish.failed') }}
          </p>
        </template>
      </div>

      <template #footer>
        <div class="flex w-full items-center justify-between gap-2">
          <Button variant="ghost" :disabled="index === 0" @click="index -= 1">
            {{ $t('site.setup.back') }}
          </Button>
          <Button v-if="onFinish" :disabled="complete.isPending.value" @click="finish">
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
        </div>
      </template>
    </PanelCard>
  </main>
</template>
