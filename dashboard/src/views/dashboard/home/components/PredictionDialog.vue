<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Without a running prediction this is the form to start one on Twitch. While one is open it shows each
// outcome with its points and viewers, and Lock is the one primary action. Once it is locked the owner picks
// the winning outcome and Resolve is the primary action. Cancel refunds every viewer, so it is red and asks first.
import { useQueryClient } from '@tanstack/vue-query';
import { useIntervalFn } from '@vueuse/core';
import { computed, ref, watchEffect } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  predictionsKey,
  useCancelPrediction,
  useLockPrediction,
  useResolvePrediction,
  useStartPrediction,
  type NewPrediction,
  type Prediction,
} from '@/api/liveOps';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Progress } from '@/components/ui/progress';
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import Plus from '~icons/nnz/add';
import Cross from '~icons/nnz/cross';

const props = defineProps<{ prediction: Prediction | null }>();

const open = defineModel<boolean>('open', { required: true });

const MIN_OUTCOMES = 2;
const MAX_OUTCOMES = 10;
const MAX_MINUTES = 30;
const SECONDS_PER_MINUTE = 60;
const REFRESH_MS = 5000;
const PERCENT = 100;

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const queries = useQueryClient();
const start = useStartPrediction(channelId);
const lock = useLockPrediction(channelId);
const resolve = useResolvePrediction(channelId);
const cancel = useCancelPrediction(channelId);

const title = ref('');
const outcomes = ref<string[]>(['', '']);
const minutes = ref('2');
const winner = ref<string | null>(null);
const confirmOpen = ref(false);

const filledOutcomes = computed(() =>
  outcomes.value.map((text: string) => text.trim()).filter((text: string) => text !== ''),
);
const minutesValue = computed(() => Number(minutes.value));
const enoughOutcomes = computed(() => filledOutcomes.value.length >= MIN_OUTCOMES);
const durationOk = computed(
  () =>
    Number.isInteger(minutesValue.value) &&
    minutesValue.value >= 1 &&
    minutesValue.value <= MAX_MINUTES,
);
const valid = computed(
  () => title.value.trim() !== '' && enoughOutcomes.value && durationOk.value,
);

const isLocked = computed(() => props.prediction?.status.toUpperCase() === 'LOCKED');
const winnerPicked = computed(() =>
  (props.prediction?.outcomes ?? []).some((outcome) => outcome.id === winner.value),
);
const totalPoints = computed(() =>
  (props.prediction?.outcomes ?? []).reduce(
    (sum: number, outcome) => sum + Number(outcome.channelPoints),
    0,
  ),
);

function share(points: number | string): number {
  return totalPoints.value > 0
    ? Math.round((Number(points) / totalPoints.value) * PERCENT)
    : 0;
}

function tally(points: number | string, users: number | string): string {
  return t('dashboard.home.quick.predictionTally', {
    points: t('dashboard.home.quick.predictionPoints', Number(points)),
    users: t('dashboard.home.quick.predictionUsers', Number(users)),
    percent: share(points),
  });
}

function reset(): void {
  title.value = '';
  outcomes.value = ['', ''];
  minutes.value = '2';
}

function submit(): void {
  if (!valid.value) return;

  const prediction: NewPrediction = {
    title: title.value.trim(),
    outcomes: filledOutcomes.value,
    predictionWindowSeconds: minutesValue.value * SECONDS_PER_MINUTE,
  };

  start.mutate(prediction, { onSuccess: reset });
}

function addOutcome(): void {
  outcomes.value = [...outcomes.value, ''];
}

function removeOutcome(index: number): void {
  outcomes.value = outcomes.value.filter((_text: string, at: number) => at !== index);
}

function resolveWinner(predictionId: string): void {
  if (!winnerPicked.value || winner.value === null) return;

  resolve.mutate({ predictionId, winningOutcomeId: winner.value });
  winner.value = null;
}

function confirmCancel(predictionId: string): void {
  confirmOpen.value = false;
  cancel.mutate(predictionId);
}

function refresh(): void {
  void queries.invalidateQueries({ queryKey: predictionsKey(channelId.value) });
}

const ticker = useIntervalFn(refresh, REFRESH_MS, { immediate: false });

watchEffect(() => {
  if (open.value && props.prediction !== null) {
    ticker.resume();
  } else {
    ticker.pause();
  }
});
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent
      :show-close-button="false"
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 data-[size=default]:sm:max-w-120"
    >
      <div class="grid flex-none gap-1.5 px-6 pt-6 pb-4">
        <DialogTitle class="text-lg font-semibold">
          {{ prediction === null ? t('dashboard.home.quick.predictionStart') : prediction.title }}
        </DialogTitle>
        <DialogDescription class="text-sm text-muted-foreground">
          {{
            isLocked
              ? t('dashboard.home.quick.predictionPickIntro')
              : t('dashboard.home.quick.predictionDialogIntro')
          }}
        </DialogDescription>
      </div>

      <div v-if="prediction === null" class="grid min-h-0 flex-1 gap-4 overflow-y-auto px-6 pb-4">
        <div class="grid gap-1.5">
          <Label for="prediction-title">{{ t('dashboard.home.quick.predictionQuestion') }}</Label>
          <Input
            id="prediction-title"
            v-model="title"
            data-prediction-field="title"
            maxlength="45"
            :placeholder="t('dashboard.home.quick.predictionQuestionHint')"
          />
        </div>

        <div class="grid gap-1.5">
          <Label>{{ t('dashboard.home.quick.predictionOutcomes') }}</Label>
          <div v-for="(_text, index) in outcomes" :key="index" class="flex items-center gap-2">
            <Input
              v-model="outcomes[index]"
              :data-prediction-outcome="index"
              maxlength="25"
              :aria-label="t('dashboard.home.quick.predictionOutcomeN', { n: index + 1 })"
              :placeholder="t('dashboard.home.quick.predictionOutcomeN', { n: index + 1 })"
            />
            <Button
              v-if="outcomes.length > MIN_OUTCOMES"
              variant="ghost"
              size="icon"
              :aria-label="t('dashboard.home.quick.predictionRemoveOutcome', { n: index + 1 })"
              @click="removeOutcome(index)"
            >
              <Cross class="size-5" />
            </Button>
          </div>
          <p v-if="!enoughOutcomes" class="text-sm text-muted-foreground">
            {{ t('dashboard.home.quick.predictionNeedOutcomes') }}
          </p>
          <div v-if="outcomes.length < MAX_OUTCOMES">
            <Button variant="ghost" size="sm" @click="addOutcome">
              <Plus />
              {{ t('dashboard.home.quick.predictionAddOutcome') }}
            </Button>
          </div>
        </div>

        <div class="grid gap-1.5">
          <Label for="prediction-minutes">{{ t('dashboard.home.quick.predictionMinutes') }}</Label>
          <Input
            id="prediction-minutes"
            v-model="minutes"
            data-prediction-field="minutes"
            type="number"
            min="1"
            :max="MAX_MINUTES"
            class="w-24"
          />
        </div>
      </div>

      <RadioGroup
        v-else
        v-model="winner"
        :disabled="!isLocked"
        class="grid min-h-0 flex-1 gap-4 overflow-y-auto px-6 pb-4"
      >
        <div
          v-for="outcome in prediction.outcomes"
          :key="outcome.id"
          :data-prediction-result="outcome.title"
          class="grid gap-1.5"
        >
          <div class="flex items-center justify-between gap-3 text-sm">
            <span class="flex items-center gap-2 font-medium">
              <RadioGroupItem
                v-if="isLocked"
                :value="outcome.id"
                :data-prediction-pick="outcome.id"
                :aria-label="t('dashboard.home.quick.predictionPickWinner', { name: outcome.title })"
              />
              {{ outcome.title }}
            </span>
            <span class="text-muted-foreground">
              {{ tally(outcome.channelPoints, outcome.users) }}
            </span>
          </div>
          <Progress :model-value="share(outcome.channelPoints)" />
        </div>
      </RadioGroup>

      <DialogFooter class="flex-none border-t border-border px-6 py-4">
        <Button variant="outline" @click="open = false">
          {{
            prediction === null
              ? t('dashboard.home.quick.pollCancel')
              : t('dashboard.home.quick.pollClose')
          }}
        </Button>
        <Button
          v-if="prediction === null"
          :disabled="!valid || start.isPending.value"
          @click="submit"
        >
          {{ t('dashboard.home.quick.predictionStartGo') }}
        </Button>
        <template v-else>
          <Button variant="destructive" :disabled="cancel.isPending.value" @click="confirmOpen = true">
            {{ t('dashboard.home.quick.predictionCancel') }}
          </Button>
          <Button
            v-if="isLocked"
            :disabled="!winnerPicked || resolve.isPending.value"
            @click="resolveWinner(prediction.id)"
          >
            {{ t('dashboard.home.quick.predictionResolve') }}
          </Button>
          <Button v-else :disabled="lock.isPending.value" @click="lock.mutate(prediction.id)">
            {{ t('dashboard.home.quick.predictionLock') }}
          </Button>
        </template>
      </DialogFooter>
    </DialogContent>
  </Dialog>

  <ConfirmDialog
    v-if="prediction !== null"
    v-model:open="confirmOpen"
    :title="t('dashboard.home.quick.predictionCancelTitle')"
    :consequences="[t('dashboard.home.quick.predictionRefund')]"
    :action="t('dashboard.home.quick.predictionCancel')"
    @confirm="confirmCancel(prediction.id)"
  />
</template>
