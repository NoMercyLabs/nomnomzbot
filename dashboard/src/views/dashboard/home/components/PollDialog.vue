<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Without a running poll this is the form to start one on Twitch; with one it shows each choice with
// its votes and share, refreshed every few seconds, and the End control. Start is the only primary
// action; End is the destructive one. Nothing is sent until the form is valid.
import { useQueryClient } from '@tanstack/vue-query';
import { useIntervalFn } from '@vueuse/core';
import { computed, ref, watchEffect } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  pollsKey,
  useEndPoll,
  useStartPoll,
  type NewPoll,
  type Poll,
} from '@/api/liveOps';
import Countdown from '@/components/layout/Countdown.vue';
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
import { Switch } from '@/components/ui/switch';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import Plus from '~icons/nnz/add';
import Cross from '~icons/nnz/cross';

const props = defineProps<{ poll: Poll | null }>();

const open = defineModel<boolean>('open', { required: true });

const MIN_CHOICES = 2;
const MAX_CHOICES = 5;
const MAX_MINUTES = 30;
const SECONDS_PER_MINUTE = 60;
const REFRESH_MS = 5000;
const PERCENT = 100;

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const queries = useQueryClient();
const start = useStartPoll(channelId);
const end = useEndPoll(channelId);

const title = ref('');
const choices = ref<string[]>(['', '']);
const minutes = ref('2');
const pointsOn = ref(false);
const pointsPerVote = ref('10');

const filledChoices = computed(() =>
  choices.value.map((text: string) => text.trim()).filter((text: string) => text !== ''),
);
const minutesValue = computed(() => Number(minutes.value));
const pointsValue = computed(() => Number(pointsPerVote.value));
const enoughChoices = computed(() => filledChoices.value.length >= MIN_CHOICES);
const durationOk = computed(
  () =>
    Number.isInteger(minutesValue.value) &&
    minutesValue.value >= 1 &&
    minutesValue.value <= MAX_MINUTES,
);
const pointsOk = computed(
  () => !pointsOn.value || (Number.isInteger(pointsValue.value) && pointsValue.value >= 1),
);
const valid = computed(
  () => title.value.trim() !== '' && enoughChoices.value && durationOk.value && pointsOk.value,
);

const totalVotes = computed(() =>
  (props.poll?.choices ?? []).reduce((sum: number, choice) => sum + Number(choice.votes), 0),
);

function share(votes: number | string): number {
  return totalVotes.value > 0
    ? Math.round((Number(votes) / totalVotes.value) * PERCENT)
    : 0;
}

function tally(votes: number | string): string {
  const count: string = t('dashboard.home.quick.pollVotes', Number(votes));

  return t('dashboard.home.quick.pollTally', { votes: count, percent: share(votes) });
}

function reset(): void {
  title.value = '';
  choices.value = ['', ''];
  minutes.value = '2';
  pointsOn.value = false;
  pointsPerVote.value = '10';
}

function submit(): void {
  if (!valid.value) return;

  const poll: NewPoll = {
    title: title.value.trim(),
    choices: filledChoices.value,
    durationSeconds: minutesValue.value * SECONDS_PER_MINUTE,
    channelPointsVotingEnabled: pointsOn.value,
    channelPointsPerVote: pointsOn.value
      ? pointsValue.value
      : 0,
  };

  start.mutate(poll, { onSuccess: reset });
}

function addChoice(): void {
  choices.value = [...choices.value, ''];
}

function removeChoice(index: number): void {
  choices.value = choices.value.filter((_text: string, at: number) => at !== index);
}

function refresh(): void {
  void queries.invalidateQueries({ queryKey: pollsKey(channelId.value) });
}

const ticker = useIntervalFn(refresh, REFRESH_MS, { immediate: false });

watchEffect(() => {
  if (open.value && props.poll !== null) {
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
          {{ poll === null ? t('dashboard.home.quick.pollStart') : poll.title }}
        </DialogTitle>
        <DialogDescription class="text-sm text-muted-foreground">
          {{ t('dashboard.home.quick.pollDialogIntro') }}
        </DialogDescription>
      </div>

      <div v-if="poll === null" class="grid min-h-0 flex-1 gap-4 overflow-y-auto px-6 pb-4">
        <div class="grid gap-1.5">
          <Label for="poll-title">{{ t('dashboard.home.quick.pollQuestion') }}</Label>
          <Input
            id="poll-title"
            v-model="title"
            data-poll-field="title"
            maxlength="60"
            :placeholder="t('dashboard.home.quick.pollQuestionHint')"
          />
        </div>

        <div class="grid gap-1.5">
          <Label>{{ t('dashboard.home.quick.pollChoices') }}</Label>
          <div v-for="(_text, index) in choices" :key="index" class="flex items-center gap-2">
            <Input
              v-model="choices[index]"
              :data-poll-choice="index"
              maxlength="25"
              :aria-label="t('dashboard.home.quick.pollChoiceN', { n: index + 1 })"
              :placeholder="t('dashboard.home.quick.pollChoiceN', { n: index + 1 })"
            />
            <Button
              v-if="choices.length > MIN_CHOICES"
              variant="ghost"
              size="icon"
              :aria-label="t('dashboard.home.quick.pollRemoveChoice', { n: index + 1 })"
              @click="removeChoice(index)"
            >
              <Cross class="size-5" />
            </Button>
          </div>
          <p v-if="!enoughChoices" class="text-sm text-muted-foreground">
            {{ t('dashboard.home.quick.pollNeedChoices') }}
          </p>
          <div v-if="choices.length < MAX_CHOICES">
            <Button variant="ghost" size="sm" @click="addChoice">
              <Plus />
              {{ t('dashboard.home.quick.pollAddChoice') }}
            </Button>
          </div>
        </div>

        <div class="grid gap-1.5">
          <Label for="poll-minutes">{{ t('dashboard.home.quick.pollMinutes') }}</Label>
          <Input
            id="poll-minutes"
            v-model="minutes"
            data-poll-field="minutes"
            type="number"
            min="1"
            :max="MAX_MINUTES"
            class="w-24"
          />
        </div>

        <div class="flex items-center justify-between gap-3">
          <Label for="poll-points">{{ t('dashboard.home.quick.pollPoints') }}</Label>
          <Switch id="poll-points" v-model="pointsOn" data-poll-field="points" />
        </div>
        <div v-if="pointsOn" class="grid gap-1.5">
          <Label for="poll-points-price">{{ t('dashboard.home.quick.pollPointsPrice') }}</Label>
          <Input
            id="poll-points-price"
            v-model="pointsPerVote"
            data-poll-field="pointsPerVote"
            type="number"
            min="1"
            class="w-32"
          />
        </div>
      </div>

      <div v-else class="grid min-h-0 flex-1 gap-4 overflow-y-auto px-6 pb-4">
        <p class="text-sm font-medium">
          <Countdown
            :target="new Date(poll.startedAt).getTime() + Number(poll.duration) * 1000"
            :label="poll.title"
          />
        </p>
        <div
          v-for="choice in poll.choices"
          :key="choice.id"
          :data-poll-result="choice.title"
          class="grid gap-1.5"
        >
          <div class="flex items-baseline justify-between gap-3 text-sm">
            <span class="font-medium">{{ choice.title }}</span>
            <span class="text-muted-foreground">{{ tally(choice.votes) }}</span>
          </div>
          <Progress :model-value="share(choice.votes)" />
        </div>
      </div>

      <DialogFooter class="flex-none border-t border-border px-6 py-4">
        <Button variant="outline" @click="open = false">
          {{
            poll === null
              ? t('dashboard.home.quick.pollCancel')
              : t('dashboard.home.quick.pollClose')
          }}
        </Button>
        <Button v-if="poll === null" :disabled="!valid || start.isPending.value" @click="submit">
          {{ t('dashboard.home.quick.pollStartGo') }}
        </Button>
        <Button v-else variant="destructive" :disabled="end.isPending.value" @click="end.mutate(poll.id)">
          {{ t('dashboard.home.quick.pollEnd') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
