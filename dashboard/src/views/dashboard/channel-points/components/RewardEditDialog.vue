<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Every field the reward update endpoint takes. The main view holds the few settings people change;
// the limits fold away in one group. A redeem answers with a simple chat reply or runs a full pipeline,
// never both: the save sends the chosen one and clears the other (RewardService.UpdateAsync:
// a reply of "" clears the reply, a pipeline id of Guid.Empty clears the pipeline).
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useUpdateReward, type Reward, type RewardChange } from '@/api/rewards';
import FormDialog from '@/components/layout/FormDialog.vue';
import ReplyModeField from '@/components/layout/ReplyModeField.vue';
import { noPipelineId, type ReplyMode } from '@/components/layout/replyMode';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import { ColorPicker } from '@/components/ui/color-picker';
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';

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
type LimitKey = Exclude<NumberKey, 'cost'>;
type FlagKey = 'isEnabled' | 'isPaused' | 'isUserInputRequired';

const props = defineProps<{ reward: Reward }>();
const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const update = useUpdateReward(channelId);

const limitKeys = [
  'maxPerStream',
  'maxPerUserPerStream',
  'globalCooldownSeconds',
  'timerDurationSeconds',
] as const satisfies LimitKey[];

const summaryKeys = {
  maxPerStream: 'limitStream',
  maxPerUserPerStream: 'limitViewer',
  globalCooldownSeconds: 'limitCooldown',
  timerDurationSeconds: 'limitTimer',
} as const satisfies Record<LimitKey, string>;

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
const mode = ref<ReplyMode>('simple');
const pipelineId = ref('');
const limitsOpen = ref(false);
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

  mode.value = reward.pipelineId
    ? 'full'
    : 'simple';

  limitsOpen.value = false;
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

function isActive(key: LimitKey): boolean {
  const value = numberOf(key);

  return value !== undefined && value > 0;
}

// One short line of the limits that are set, so the folded group still tells what is on.
const limitsSummary = computed(() => {
  const parts = limitKeys
    .filter(isActive)
    .map((key: LimitKey): string => t(`dashboard.channelPoints.rewards.dialog.${summaryKeys[key]}`, { count: numberOf(key) }));

  if (flags.value.isPaused) {
    parts.push(t('dashboard.channelPoints.rewards.dialog.limitPaused'));
  }

  return parts.length === 0
    ? t('dashboard.channelPoints.rewards.dialog.limitsNone')
    : parts.join(' · ');
});

// Simple mode sends the typed reply and clears the pipeline; pipeline mode sends the pipeline and clears the reply.
function replyChange(): Pick<RewardChange, 'response' | 'pipelineId'> {
  return mode.value === 'simple'
    ? { response: text.value.response, pipelineId: noPipelineId }
    : { response: '', pipelineId: pipelineId.value || undefined };
}

function changeOf(): RewardChange {
  const { response: _reply, ...fields } = text.value;

  return {
    ...fields,
    ...flags.value,
    ...replyChange(),
    cost: numberOf('cost'),
    maxPerStream: numberOf('maxPerStream'),
    maxPerUserPerStream: numberOf('maxPerUserPerStream'),
    globalCooldownSeconds: numberOf('globalCooldownSeconds'),
    timerDurationSeconds: numberOf('timerDurationSeconds'),
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
    class="sm:max-w-3xl"
    data-reward-dialog
    @submit="submit"
  >
    <template #header-action>
      <div class="flex flex-none items-center gap-4">
        <label class="flex items-center gap-2 text-sm font-medium">
          {{ $t('dashboard.channelPoints.rewards.dialog.enabledSwitch') }}
          <Switch
            v-model="flags.isEnabled"
            data-reward-dialog-enabled
            :disabled="!reward.isManageable"
          />
        </label>
        <label class="flex items-center gap-2 text-sm font-medium">
          {{ $t('dashboard.channelPoints.rewards.dialog.pausedSwitch') }}
          <Switch
            v-model="flags.isPaused"
            data-reward-dialog-paused
            data-field="isPaused"
            :disabled="!reward.isManageable"
          />
        </label>
      </div>
    </template>

    <p
      v-if="!reward.isManageable"
      data-reward-unmanageable
      class="rounded-md bg-muted px-3 py-2 text-sm text-muted-foreground"
    >
      {{ $t('dashboard.channelPoints.rewards.dialog.unmanageable') }}
    </p>

    <div class="grid grid-cols-1 gap-x-4 gap-y-3 md:grid-cols-3">
      <Field class="md:col-span-2">
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

      <Field>
        <FieldLabel for="reward-cost">{{ $t('dashboard.channelPoints.rewards.dialog.fields.cost') }}</FieldLabel>
        <Input
          id="reward-cost"
          v-model="numbers.cost"
          type="number"
          min="0"
          inputmode="numeric"
          data-field="cost"
          :disabled="!reward.isManageable"
        />
        <FieldDescription>{{ $t('dashboard.channelPoints.rewards.dialog.hints.cost') }}</FieldDescription>
      </Field>

      <Field class="md:col-span-3">
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

      <Field class="md:col-span-3">
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

      <Field orientation="horizontal" class="md:col-span-3">
        <Switch
          id="reward-isUserInputRequired"
          v-model="flags.isUserInputRequired"
          data-field="isUserInputRequired"
          :disabled="!reward.isManageable"
        />
        <div class="grid gap-1">
          <FieldLabel for="reward-isUserInputRequired">{{ $t('dashboard.channelPoints.rewards.dialog.fields.isUserInputRequired') }}</FieldLabel>
          <FieldDescription>{{ $t('dashboard.channelPoints.rewards.dialog.hints.isUserInputRequired') }}</FieldDescription>
        </div>
      </Field>

      <div class="grid gap-2 md:col-span-3">
        <ReplyModeField v-model:mode="mode" v-model:pipeline-id="pipelineId">
          <Field>
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
        </ReplyModeField>
      </div>
    </div>

    <Collapsible v-model:open="limitsOpen" class="rounded-lg border border-border">
      <CollapsibleTrigger
        data-limits-trigger
        class="group flex w-full items-center justify-between gap-3 rounded-lg px-3 py-2 text-left text-sm"
      >
        <span class="font-medium">{{ $t('dashboard.channelPoints.rewards.dialog.limits') }}</span>
        <span class="ml-auto truncate text-muted-foreground">{{ limitsSummary }}</span>
        <ChevronDown
          data-limits-chevron
          class="size-4 shrink-0 text-muted-foreground transition-transform group-data-[state=open]:rotate-180"
        />
      </CollapsibleTrigger>

      <CollapsibleContent>
        <div class="grid grid-cols-1 gap-x-4 gap-y-3 border-t border-border p-3 md:grid-cols-2">
          <Field v-for="key in limitKeys" :key="key">
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
        </div>
      </CollapsibleContent>
    </Collapsible>
  </FormDialog>
</template>
