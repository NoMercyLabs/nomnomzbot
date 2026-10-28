<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Every field the reward update endpoint takes. The main view holds the few settings people change;
// the limits fold away in one group. A redeem answers with a simple chat reply or runs a full pipeline,
// never both: the save sends the chosen one and clears the other (RewardService.UpdateAsync:
// a reply of "" clears the reply, a pipeline id of Guid.Empty clears the pipeline).
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  fetchRewardBlastRadius,
  useCreateReward,
  useDeleteReward,
  useUpdateReward,
  type Reward,
  type RewardChange,
} from '@/api/rewards';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import FormDialog from '@/components/layout/FormDialog.vue';
import ReplyDeliveryField from '@/components/layout/ReplyDeliveryField.vue';
import ReplyModeField from '@/components/layout/ReplyModeField.vue';
import {
  deliveryOf,
  deliveryToSave,
  noPipelineId,
  plainDelivery,
  type ReplyDelivery,
  type ReplyMode,
} from '@/components/layout/replyMode';
import { Button } from '@/components/ui/button';
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible';
import { ColorPicker } from '@/components/ui/color-picker';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Field, FieldCount, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';

import ChevronDown from '~icons/nnz/chevron-down';

import { Switch } from '@/components/ui/switch';
import { Textarea } from '@/components/ui/textarea';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { boundTarget, targetToWire, type PickedTarget } from '@/lib/bindingTarget';
import { errorText, fieldErrorLines } from '@/lib/errors';
import { blastLines } from '@/views/dashboard/scripts/components/blastLines';

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

const { t, te } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const update = useUpdateReward(channelId);
const create = useCreateReward(channelId);
const remove = useDeleteReward(channelId);

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
// Where the typed reply goes: Twitch chat, the chat overlay, both, or nowhere.
const delivery = ref<ReplyDelivery>({ ...plainDelivery });
// What the reward runs now (a pipeline, a command or an action) and what the form picked.
const bound = ref<PickedTarget | null>(null);
const target = ref<PickedTarget | null>(null);
// Twitch's limits: a reward title holds 45 characters, its description 200, a chat message 500.
const TITLE_MAX = 45;
const PROMPT_MAX = 200;
const RESPONSE_MAX = 500;
// The limits start shut each time the dialog opens, to keep the form short.
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

  delivery.value = deliveryOf(reward.replyDelivery);
  bound.value = boundTarget(reward.target, reward.pipelineId);
  target.value = bound.value;

  mode.value = bound.value !== null
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
    .map((key): string => t(`dashboard.channelPoints.rewards.dialog.${summaryKeys[key]}`, { count: numberOf(key) }));

  if (flags.value.isPaused) {
    parts.push(t('dashboard.channelPoints.rewards.dialog.limitPaused'));
  }

  return parts.length === 0
    ? t('dashboard.channelPoints.rewards.dialog.limitsNone')
    : parts.join(' · ');
});

// Simple mode sends the typed reply and clears the binding; pipeline mode sends the target and clears the reply.
// A pipeline travels as its id, as before; a command or action travels as a target, and a target wins over the id.
// The bot reads no delivery as "unchanged", so the simple reply always sends its target, chat included.
function replyChange(): Pick<RewardChange, 'response' | 'replyDelivery' | 'pipelineId' | 'target'> {
  const leavingOther = bound.value !== null && bound.value.kind !== 'pipeline';

  if (mode.value === 'simple') {
    return {
      response: text.value.response,
      replyDelivery: deliveryToSave(delivery.value),
      pipelineId: noPipelineId,
      ...(leavingOther
        ? { target: targetToWire(null) }
        : {}),
    };
  }

  if (target.value?.kind === 'pipeline' && !leavingOther)
    return { response: '', pipelineId: target.value.id };

  return { response: '', target: targetToWire(target.value) };
}

function changeOf() {
  const { response: _reply, ...fields } = text.value;

  return {
    ...fields,
    ...flags.value,
    ...replyChange(),
    // Twitch refuses an empty color (400), so an empty field leaves the color as it is.
    backgroundColor: fields.backgroundColor || undefined,
    cost: numberOf('cost'),
    maxPerStream: numberOf('maxPerStream'),
    maxPerUserPerStream: numberOf('maxPerUserPerStream'),
    globalCooldownSeconds: numberOf('globalCooldownSeconds'),
    timerDurationSeconds: numberOf('timerDurationSeconds'),
  } as const satisfies RewardChange;
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

// Delete reads what the reward takes with it first and asks; nothing is deleted before the owner confirms.
const confirmOpen = ref(false);
const consequences = ref<string[]>([]);

async function askDelete(): Promise<void> {
  error.value = undefined;

  try {
    const lines = blastLines(
      await fetchRewardBlastRadius(channelId.value as string, props.reward.id),
      t,
      te,
    );

    consequences.value = lines.length > 0
      ? lines
      : [t('dashboard.channelPoints.rewards.delete.nothing')];

    confirmOpen.value = true;
  } catch (failure) {
    error.value = errorText(failure, t);
  }
}

async function confirmDelete(): Promise<void> {
  try {
    await remove.mutateAsync(props.reward.id);
  } catch (failure) {
    error.value = errorText(failure, t);
    confirmOpen.value = false;

    return;
  }

  confirmOpen.value = false;
  open.value = false;
}

// The copy has the saved settings of this reward and starts switched off, so it never goes live by surprise.
// Twitch keeps reward names unique, so the copy gets a " copy" tail inside the title limit.
async function duplicate(): Promise<void> {
  const tail = ` ${t('dashboard.channelPoints.rewards.duplicate.tail')}`;
  const { reward } = props;

  error.value = undefined;

  try {
    const copyId = await create.mutateAsync({
      title: `${reward.title.slice(0, TITLE_MAX - tail.length)}${tail}`,
      cost: reward.cost,
      prompt: reward.prompt,
      response: reward.response,
      replyDelivery: reward.replyDelivery,
      isUserInputRequired: reward.isUserInputRequired,
      backgroundColor: reward.backgroundColor,
      maxPerStream: reward.maxPerStream,
      maxPerUserPerStream: reward.maxPerUserPerStream,
      globalCooldownSeconds: reward.globalCooldownSeconds,
      timerDurationSeconds: reward.timerDurationSeconds,
      speaksViaTts: reward.speaksViaTts,
      pipelineId: reward.pipelineId,
      target: reward.target,
    });

    if (copyId !== null) {
      await update.mutateAsync({ rewardId: copyId, change: { isEnabled: false } });
    }
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
    <template v-if="can('reward:manage')" #footer-start>
      <Button
        type="button"
        variant="outline"
        :disabled="update.isPending.value || remove.isPending.value"
        data-reward-delete
        @click="askDelete"
      >
        {{ $t('dashboard.channelPoints.rewards.delete.button') }}
      </Button>
      <Button
        type="button"
        variant="outline"
        :disabled="update.isPending.value || create.isPending.value"
        data-reward-duplicate
        @click="duplicate"
      >
        {{ $t('dashboard.channelPoints.rewards.duplicate.button') }}
      </Button>
    </template>

    <template #header-action>
      <div
        v-if="can('reward:manage')"
        class="flex flex-none items-center gap-4"
      >
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

    <div class="grid grid-cols-1 gap-x-4 gap-y-6 md:grid-cols-3">
      <Field class="md:col-span-2">
        <div class="flex items-center gap-1.5">
          <FieldLabel for="reward-title">{{ $t('dashboard.channelPoints.rewards.dialog.fields.title') }}</FieldLabel>
          <FieldInfo>{{ $t('dashboard.channelPoints.rewards.dialog.hints.title') }}</FieldInfo>
          <FieldCount :value="text.title" :max="TITLE_MAX" />
        </div>
        <Input
          id="reward-title"
          :maxlength="TITLE_MAX"
          v-model="text.title"
          data-field="title"
          autocomplete="off"
          :disabled="!reward.isManageable"
        />
      </Field>

      <Field>
        <div class="flex items-center gap-1.5">
          <FieldLabel for="reward-cost">{{ $t('dashboard.channelPoints.rewards.dialog.fields.cost') }}</FieldLabel>
          <FieldInfo>{{ $t('dashboard.channelPoints.rewards.dialog.hints.cost') }}</FieldInfo>
        </div>
        <Input
          id="reward-cost"
          v-model="numbers.cost"
          type="number"
          min="0"
          inputmode="numeric"
          data-field="cost"
          :disabled="!reward.isManageable"
        />
      </Field>

      <Field class="md:col-span-3">
        <div class="flex items-center gap-1.5">
          <FieldLabel for="reward-prompt">{{ $t('dashboard.channelPoints.rewards.dialog.fields.prompt') }}</FieldLabel>
          <FieldInfo>{{ $t('dashboard.channelPoints.rewards.dialog.hints.prompt') }}</FieldInfo>
          <FieldCount :value="text.prompt" :max="PROMPT_MAX" />
        </div>
        <Textarea
          id="reward-prompt"
          :maxlength="PROMPT_MAX"
          rows="2"
          class="min-h-0"
          v-model="text.prompt"
          data-field="prompt"
          :disabled="!reward.isManageable"
        />
      </Field>

      <Field>
        <div class="flex items-center gap-1.5">
          <FieldLabel for="reward-backgroundColor">{{ $t('dashboard.channelPoints.rewards.dialog.fields.backgroundColor') }}</FieldLabel>
          <FieldInfo>{{ $t('dashboard.channelPoints.rewards.dialog.hints.backgroundColor') }}</FieldInfo>
        </div>
        <ColorPicker
          id="reward-backgroundColor"
          v-model="text.backgroundColor"
          data-field="backgroundColor"
          placeholder="#9146FF"
          :label="$t('dashboard.channelPoints.rewards.dialog.fields.backgroundColor')"
          :disabled="!reward.isManageable"
        />
      </Field>

      <Field
        v-if="can('reward:manage')"
        class="md:col-span-2"
      >
        <div class="flex items-center gap-1.5">
          <FieldLabel for="reward-isUserInputRequired">{{ $t('dashboard.channelPoints.rewards.dialog.fields.isUserInputRequired') }}</FieldLabel>
          <FieldInfo>{{ $t('dashboard.channelPoints.rewards.dialog.hints.isUserInputRequired') }}</FieldInfo>
        </div>
        <div class="flex h-9 items-center">
          <Switch
            id="reward-isUserInputRequired"
            v-model="flags.isUserInputRequired"
            data-field="isUserInputRequired"
            :disabled="!reward.isManageable"
          />
        </div>
      </Field>

      <div class="grid gap-2 md:col-span-3">
        <ReplyModeField
          v-model:mode="mode"
          v-model:target="target"
          :kinds="['command', 'pipeline', 'action']"
          :new-pipeline-name="reward.title"
          :disabled="!reward.isManageable"
        >
          <TemplateField
            id="reward-response"
            v-model="text.response"
            context="eventResponse"
            event-type="channel.channel_points_custom_reward_redemption.add"
            data-field="response"
            :rows="1"
            :maxlength="RESPONSE_MAX"
            :label="$t('dashboard.channelPoints.rewards.dialog.fields.response')"
            :help="$t('dashboard.channelPoints.rewards.dialog.hints.response')"
            :disabled="!reward.isManageable"
          />
          <!-- Where the typed reply goes belongs to the simple reply; a pipeline sends through its own step. -->
          <ReplyDeliveryField v-model="delivery" :disabled="!reward.isManageable" />
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
            <div class="flex items-center gap-1.5">
              <FieldLabel :for="`reward-${key}`">{{ $t(`dashboard.channelPoints.rewards.dialog.fields.${key}`) }}</FieldLabel>
              <FieldInfo>{{ $t(`dashboard.channelPoints.rewards.dialog.hints.${key}`) }}</FieldInfo>
            </div>
            <Input
              :id="`reward-${key}`"
              v-model="numbers[key]"
              type="number"
              min="0"
              inputmode="numeric"
              :data-field="key"
              :disabled="!reward.isManageable"
            />
          </Field>
        </div>
      </CollapsibleContent>
    </Collapsible>
  </FormDialog>

  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="$t('dashboard.channelPoints.rewards.delete.title', { title: reward.title })"
    :description="$t('dashboard.channelPoints.rewards.delete.description')"
    :consequences="consequences"
    :action="$t('dashboard.channelPoints.rewards.delete.action')"
    :pending="remove.isPending.value"
    @confirm="confirmDelete"
  />
</template>
