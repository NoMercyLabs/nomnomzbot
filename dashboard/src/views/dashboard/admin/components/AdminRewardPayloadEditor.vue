<!--
  -----------------------------------------------------------------------------
   Copyright (c) NoMercy Labs.

   This file is part of NomNomzBot. You may use, change and share it for free,
   for any noncommercial purpose, under the PolyForm Noncommercial License
   1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
   See LICENSE for details, and TRADEMARKS.md for the name and logo.

   SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
  -----------------------------------------------------------------------------
-->
<script setup lang="ts">
// A reward definition's draft with the fields of the channel's reward editor: the name, the cost, the description,
// the color, the chat reply, the limits and the switches. Save keeps the whole payload as a new version and is the
// one primary action; Edit JSON is the raw fallback.
import Cross from '~icons/nnz/cross';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import InlineNotice from '@/components/layout/InlineNotice.vue';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Button } from '@/components/ui/button';
import { ColorPicker } from '@/components/ui/color-picker';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from '@/components/ui/dialog';
import { Field, FieldCount, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { ensureWords } from '@/i18n';

import {
  limitKeys,
  rewardLimits,
  rewardPayloadProblem,
  rewardWords,
  writeRewardPayload,
  type LimitKey,
  type RewardPayload,
} from './rewardPayload';

const props = defineProps<{
  title: string;
  payload: RewardPayload;
  // Keeps the payload; resolves to the message shown after Save.
  keep: (payloadJson: string) => Promise<string>;
}>();

const emit = defineEmits<{
  close: [];
  // Hands the draft over as JSON for the raw editor.
  editJson: [payloadJson: string];
}>();

const { t } = useI18n();

const draft = ref<RewardPayload>({ ...props.payload });

const wordsReady = ref(false);
const message = ref('');
const saving = ref(false);

void ensureWords(rewardWords).then(() => {
  wordsReady.value = true;
});

const problem = computed(() => rewardPayloadProblem(draft.value));
const problemText = computed(() => {
  const found = problem.value;

  if (found === null)
    return '';

  const names = {
    title: { key: 'title', max: rewardLimits.titleMax },
    prompt: { key: 'prompt', max: rewardLimits.promptMax },
    response: { key: 'response', max: rewardLimits.responseMax },
  } as const;

  if (found === 'title' || found === 'prompt' || found === 'response')
    return t(`dashboard.admin.content.rewardEditor.problem.${names[found].key}`, { max: names[found].max });

  if (found === 'cost' || found === 'color')
    return t(`dashboard.admin.content.rewardEditor.problem.${found}`);

  return t('dashboard.admin.content.rewardEditor.problem.limit', {
    cooldown: rewardLimits.cooldownMax,
    timer: rewardLimits.timerMax,
  });
});

// A number field hands over a number, the draft keeps the text so a blank field stays blank.
function setNumber(key: 'cost' | LimitKey, value: string | number): void {
  draft.value[key] = String(value);
}

function currentJson(): string {
  return writeRewardPayload(draft.value);
}

async function save(): Promise<void> {
  saving.value = true;
  message.value = await props.keep(currentJson());
  saving.value = false;
}
</script>

<template>
  <Dialog :open="true" @update:open="emit('close')">
    <DialogContent
      data-reward-payload-editor
      :show-close-button="false"
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 sm:max-w-2xl"
    >
      <div class="flex flex-none items-start justify-between gap-4 px-6 pt-6 pb-4">
        <div class="grid gap-1.5">
          <DialogTitle class="text-lg font-semibold">{{ title }}</DialogTitle>
          <DialogDescription class="text-sm text-muted-foreground">
            {{ $t("dashboard.admin.content.rewardEditor.description") }}
          </DialogDescription>
        </div>
        <DialogClose as-child>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            :aria-label="$t('common.close')"
          >
            <Cross class="size-5" aria-hidden="true" />
          </Button>
        </DialogClose>
      </div>
      <div v-if="wordsReady" class="flex min-h-0 flex-1 flex-col gap-5 overflow-y-auto px-6 pb-4">
        <div class="grid grid-cols-1 gap-x-4 gap-y-5 md:grid-cols-3">
          <Field class="md:col-span-2">
            <div class="flex items-center gap-1.5">
              <FieldLabel for="reward-title">{{
                $t("dashboard.channelPoints.rewards.dialog.fields.title")
              }}</FieldLabel>
              <FieldInfo>{{ $t("dashboard.channelPoints.rewards.dialog.hints.title") }}</FieldInfo>
              <FieldCount :value="draft.title" :max="rewardLimits.titleMax" />
            </div>
            <Input
              id="reward-title"
              v-model="draft.title"
              :maxlength="rewardLimits.titleMax"
              autocomplete="off"
            />
          </Field>

          <Field>
            <div class="flex items-center gap-1.5">
              <FieldLabel for="reward-cost">{{
                $t("dashboard.channelPoints.rewards.dialog.fields.cost")
              }}</FieldLabel>
              <FieldInfo>{{ $t("dashboard.channelPoints.rewards.dialog.hints.cost") }}</FieldInfo>
            </div>
            <Input
              id="reward-cost"
              :model-value="draft.cost"
              type="number"
              min="1"
              inputmode="numeric"
              :aria-invalid="problem === 'cost'"
              @update:model-value="setNumber('cost', $event)"
            />
          </Field>

          <Field class="md:col-span-3">
            <div class="flex items-center gap-1.5">
              <FieldLabel for="reward-prompt">{{
                $t("dashboard.channelPoints.rewards.dialog.fields.prompt")
              }}</FieldLabel>
              <FieldInfo>{{ $t("dashboard.channelPoints.rewards.dialog.hints.prompt") }}</FieldInfo>
              <FieldCount :value="draft.prompt" :max="rewardLimits.promptMax" />
            </div>
            <Textarea
              id="reward-prompt"
              v-model="draft.prompt"
              :maxlength="rewardLimits.promptMax"
              rows="2"
              class="min-h-0"
            />
          </Field>

          <Field>
            <div class="flex items-center gap-1.5">
              <FieldLabel for="reward-backgroundColor">{{
                $t("dashboard.channelPoints.rewards.dialog.fields.backgroundColor")
              }}</FieldLabel>
              <FieldInfo>{{
                $t("dashboard.channelPoints.rewards.dialog.hints.backgroundColor")
              }}</FieldInfo>
            </div>
            <ColorPicker
              id="reward-backgroundColor"
              v-model="draft.backgroundColor"
              placeholder="#9146FF"
              :label="$t('dashboard.channelPoints.rewards.dialog.fields.backgroundColor')"
            />
          </Field>

          <div class="md:col-span-2">
            <TemplateField
              id="reward-response"
              v-model="draft.response"
              context="eventResponse"
              event-type="channel.channel_points_custom_reward_redemption.add"
              :rows="1"
              :panel="false"
              :label="$t('dashboard.channelPoints.rewards.dialog.fields.response')"
              :help="$t('dashboard.channelPoints.rewards.dialog.hints.response')"
            />
          </div>
        </div>

        <div class="grid gap-3">
          <p class="text-sm font-medium">{{ $t("dashboard.admin.content.rewardEditor.limits") }}</p>
          <div class="grid grid-cols-1 gap-x-4 gap-y-3 md:grid-cols-2">
            <Field v-for="key in limitKeys" :key="key">
              <div class="flex items-center gap-1.5">
                <FieldLabel :for="`reward-${key}`">{{
                  $t(`dashboard.channelPoints.rewards.dialog.fields.${key}`)
                }}</FieldLabel>
                <FieldInfo>{{ $t(`dashboard.channelPoints.rewards.dialog.hints.${key}`) }}</FieldInfo>
              </div>
              <Input
                :id="`reward-${key}`"
                :model-value="draft[key]"
                type="number"
                min="1"
                inputmode="numeric"
                @update:model-value="setNumber(key, $event)"
              />
            </Field>
          </div>
        </div>

        <div class="flex flex-wrap gap-x-8 gap-y-3">
          <LabeledSwitch
            v-model="draft.isUserInputRequired"
            :label="$t('dashboard.channelPoints.rewards.dialog.fields.isUserInputRequired')"
            :help="$t('dashboard.channelPoints.rewards.dialog.hints.isUserInputRequired')"
          />
          <LabeledSwitch
            v-model="draft.speaksViaTts"
            :label="$t('dashboard.admin.content.rewardEditor.speaksViaTts')"
            :help="$t('dashboard.admin.content.rewardEditor.speaksViaTtsHelp')"
          />
        </div>

        <InlineNotice v-if="problemText" data-reward-problem tone="error" compact>
          {{ problemText }}
        </InlineNotice>
      </div>
      <p
        v-if="message"
        data-reward-message
        class="flex-none px-6 pb-4 text-sm text-muted-foreground"
        role="status"
      >
        {{ message }}
      </p>
      <DialogFooter class="flex-none border-t border-border px-6 py-4">
        <Button
          data-reward-edit-json
          type="button"
          variant="outline"
          class="sm:mr-auto"
          @click="emit('editJson', currentJson())"
        >
          {{ $t("dashboard.admin.content.rewardEditor.editJson") }}
        </Button>
        <Button
          data-reward-save
          type="button"
          :disabled="saving || problem !== null"
          @click="save"
        >
          {{ $t("dashboard.admin.content.draft.save") }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
