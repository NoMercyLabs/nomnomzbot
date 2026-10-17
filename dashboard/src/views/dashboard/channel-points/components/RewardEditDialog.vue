<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Every field the reward update endpoint takes, each with one line on what it changes.
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { usePipelines } from '@/api/pipelines';
import { useUpdateReward, type Reward, type RewardChange } from '@/api/rewards';
import FormDialog from '@/components/layout/FormDialog.vue';
import { ColorPicker } from '@/components/ui/color-picker';
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Switch } from '@/components/ui/switch';
import { Textarea } from '@/components/ui/textarea';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

type TextKey = 'title' | 'prompt' | 'response' | 'backgroundColor';
type NumberKey =
  | 'cost' |
  'maxPerStream' |
  'maxPerUserPerStream' |
  'globalCooldownSeconds' |
  'timerDurationSeconds';
type FlagKey = 'isEnabled' | 'isPaused' | 'isUserInputRequired';

const props = defineProps<{ reward: Reward }>();
const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const update = useUpdateReward(channelId);
const pipelines = usePipelines(channelId);

const numberKeys = [
  'cost',
  'maxPerStream',
  'maxPerUserPerStream',
  'globalCooldownSeconds',
  'timerDurationSeconds',
] as const satisfies NumberKey[];
const flagKeys = ['isEnabled', 'isPaused', 'isUserInputRequired'] as const satisfies FlagKey[];

const text = ref<Record<TextKey, string>>({
  title: '',
  prompt: '',
  response: '',
  backgroundColor: '',
});
const numbers = ref<Record<NumberKey, string>>({
  cost: '',
  maxPerStream: '',
  maxPerUserPerStream: '',
  globalCooldownSeconds: '',
  timerDurationSeconds: '',
});
const flags = ref<Record<FlagKey, boolean>>({
  isEnabled: false,
  isPaused: false,
  isUserInputRequired: false,
});
const pipelineId = ref('');
const error = ref<string | undefined>(undefined);

function numberText(value: number | string | null | undefined): string {
  return value === null || value === undefined
    ? ''
    : String(value);
}

function load(reward: Reward): void {
  text.value = {
    title: reward.title,
    prompt: reward.prompt ?? '',
    response: reward.response ?? '',
    backgroundColor: reward.backgroundColor ?? '',
  };

  numbers.value = {
    cost: numberText(reward.cost),
    maxPerStream: numberText(reward.maxPerStream),
    maxPerUserPerStream: numberText(reward.maxPerUserPerStream),
    globalCooldownSeconds: numberText(reward.globalCooldownSeconds),
    timerDurationSeconds: numberText(reward.timerDurationSeconds),
  };

  flags.value = {
    isEnabled: reward.isEnabled,
    isPaused: reward.isPaused,
    isUserInputRequired: reward.isUserInputRequired,
  };

  pipelineId.value = reward.pipelineId ?? '';
  error.value = undefined;
}

watch(open, (isOpen: boolean): void => {
  if (isOpen) {
    load(props.reward);
  }
}, { immediate: true });

// A blank number is left out of the request, which leaves that setting as it is.
function numberOf(key: NumberKey): number | undefined {
  const raw = String(numbers.value[key]).trim();

  return raw === ''
    ? undefined
    : Number(raw);
}

function changeOf(): RewardChange {
  return {
    ...text.value,
    ...flags.value,
    cost: numberOf('cost'),
    maxPerStream: numberOf('maxPerStream'),
    maxPerUserPerStream: numberOf('maxPerUserPerStream'),
    globalCooldownSeconds: numberOf('globalCooldownSeconds'),
    timerDurationSeconds: numberOf('timerDurationSeconds'),
    pipelineId: pipelineId.value || undefined,
  };
}

async function submit(): Promise<void> {
  if (text.value.title.trim() === '') {
    error.value = t('dashboard.channelPoints.rewards.dialog.titleRequired');

    return;
  }

  error.value = undefined;

  try {
    await update.mutateAsync({ rewardId: props.reward.id, change: changeOf() });
  } catch (failure) {
    error.value = [errorText(failure, t), ...fieldErrorLines(failure)].join(' ');

    return;
  }

  open.value = false;
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    :title="$t('dashboard.channelPoints.rewards.dialog.title', { title: reward.title })"
    :description="$t('dashboard.channelPoints.rewards.dialog.description')"
    :action="$t('dashboard.channelPoints.rewards.dialog.save')"
    :pending="update.isPending.value"
    :error="error"
    class="sm:max-w-3xl lg:max-w-5xl"
    data-reward-dialog
    @submit="submit"
  >
    <p
      v-if="!reward.isManageable"
      data-reward-unmanageable
      class="rounded-md bg-muted px-3 py-2 text-sm text-muted-foreground"
    >
      {{ $t('dashboard.channelPoints.rewards.dialog.unmanageable') }}
    </p>

    <div class="grid grid-cols-1 gap-x-4 gap-y-3 md:grid-cols-2 lg:grid-cols-4">
      <Field class="order-1 md:col-span-2">
        <FieldLabel for="reward-title">{{ $t('dashboard.channelPoints.rewards.dialog.fields.title') }}</FieldLabel>
        <Input
          id="reward-title"
          v-model="text.title"
          data-field="title"
          autocomplete="off"
          :disabled="!reward.isManageable"
        />
        <FieldDescription>{{ $t('dashboard.channelPoints.rewards.dialog.hints.title') }}</FieldDescription>
      </Field>

      <Field class="order-4 md:col-span-2">
        <FieldLabel for="reward-prompt">{{ $t('dashboard.channelPoints.rewards.dialog.fields.prompt') }}</FieldLabel>
        <Textarea
          id="reward-prompt"
          rows="2"
          class="min-h-0"
          v-model="text.prompt"
          data-field="prompt"
          :disabled="!reward.isManageable"
        />
        <FieldDescription>{{ $t('dashboard.channelPoints.rewards.dialog.hints.prompt') }}</FieldDescription>
      </Field>

      <Field class="order-5 md:col-span-2">
        <FieldLabel for="reward-response">{{ $t('dashboard.channelPoints.rewards.dialog.fields.response') }}</FieldLabel>
        <Textarea
          id="reward-response"
          rows="2"
          class="min-h-0"
          v-model="text.response"
          data-field="response"
          :disabled="!reward.isManageable"
        />
        <FieldDescription>{{ $t('dashboard.channelPoints.rewards.dialog.hints.response') }}</FieldDescription>
      </Field>

      <Field
        v-for="key in numberKeys"
        :key="key"
        :class="key === 'cost' ? 'order-2' : 'order-6'"
      >
        <FieldLabel :for="`reward-${key}`">{{ $t(`dashboard.channelPoints.rewards.dialog.fields.${key}`) }}</FieldLabel>
        <Input
          :id="`reward-${key}`"
          v-model="numbers[key]"
          type="number"
          min="0"
          inputmode="numeric"
          :data-field="key"
          :disabled="!reward.isManageable"
        />
        <FieldDescription>{{ $t(`dashboard.channelPoints.rewards.dialog.hints.${key}`) }}</FieldDescription>
      </Field>

      <Field class="order-3">
        <FieldLabel for="reward-backgroundColor">{{ $t('dashboard.channelPoints.rewards.dialog.fields.backgroundColor') }}</FieldLabel>
        <ColorPicker
          id="reward-backgroundColor"
          v-model="text.backgroundColor"
          data-field="backgroundColor"
          placeholder="#9146FF"
          :label="$t('dashboard.channelPoints.rewards.dialog.fields.backgroundColor')"
          :disabled="!reward.isManageable"
        />
        <FieldDescription>{{ $t('dashboard.channelPoints.rewards.dialog.hints.backgroundColor') }}</FieldDescription>
      </Field>

      <Field v-for="key in flagKeys" :key="key" orientation="horizontal" class="order-7">
        <Switch
          :id="`reward-${key}`"
          v-model="flags[key]"
          :data-field="key"
          :disabled="!reward.isManageable"
        />
        <div class="grid gap-1">
          <FieldLabel :for="`reward-${key}`">{{ $t(`dashboard.channelPoints.rewards.dialog.fields.${key}`) }}</FieldLabel>
          <FieldDescription>{{ $t(`dashboard.channelPoints.rewards.dialog.hints.${key}`) }}</FieldDescription>
        </div>
      </Field>

      <Field class="order-8 md:col-span-2 lg:col-span-1">
        <FieldLabel>{{ $t('dashboard.channelPoints.rewards.dialog.fields.pipelineId') }}</FieldLabel>
        <Select v-model="pipelineId">
          <SelectTrigger
            data-field="pipelineId"
            class="w-full"
            :aria-label="$t('dashboard.channelPoints.rewards.dialog.fields.pipelineId')"
            :disabled="!reward.isManageable || pipelines.isError.value"
          >
            <SelectValue :placeholder="$t('dashboard.channelPoints.rewards.dialog.pipelineNone')" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem
              v-for="pipeline in pipelines.data.value ?? []"
              :key="pipeline.id"
              :value="pipeline.id"
            >
              {{ pipeline.name }}
            </SelectItem>
          </SelectContent>
        </Select>
        <FieldDescription>{{
          pipelines.isError.value
            ? $t('dashboard.channelPoints.rewards.dialog.pipelineUnavailable')
            : $t('dashboard.channelPoints.rewards.dialog.hints.pipelineId')
        }}</FieldDescription>
      </Field>
    </div>
  </FormDialog>
</template>
