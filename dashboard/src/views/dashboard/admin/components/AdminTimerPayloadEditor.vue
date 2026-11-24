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
// A timer definition's draft with the fields of the channel's timer editor: the name, the lines it posts, how often
// and how busy chat must be, and the Post once and Enabled switches. Save keeps the whole payload as a new version
// and is the one primary action; Edit JSON is the raw fallback.
import Cross from '~icons/nnz/cross';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import InlineNotice from '@/components/layout/InlineNotice.vue';
import LabeledSwitch from '@/components/layout/LabeledSwitch.vue';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from '@/components/ui/dialog';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { ensureWords } from '@/i18n';
import {
  defaultChatLines,
  defaultMinutes,
  timerLimits,
} from '@/views/dashboard/timers/components/timerDraft';

import {
  timerPayloadProblem,
  timerWords,
  writeTimerPayload,
  type TimerPayload,
} from './timerPayload';

const props = defineProps<{
  title: string;
  payload: TimerPayload;
  // Keeps the payload; resolves to the message shown after Save.
  keep: (payloadJson: string) => Promise<string>;
}>();

const emit = defineEmits<{
  close: [];
  // Hands the draft over as JSON for the raw editor.
  editJson: [payloadJson: string];
}>();

const { t } = useI18n();

const draft = ref<TimerPayload>({ ...props.payload });

const wordsReady = ref(false);
const message = ref('');
const saving = ref(false);

void ensureWords(timerWords).then(() => {
  wordsReady.value = true;
});

const problem = computed(() => timerPayloadProblem(draft.value));
const problemText = computed(() => {
  if (problem.value === 'name')
    return t('dashboard.admin.content.timerEditor.nameRequired');

  if (problem.value === 'interval')
    return t('dashboard.timers.editor.intervalProblem', timerLimits);

  if (problem.value === 'activity')
    return t('dashboard.timers.editor.activityProblem', timerLimits);

  return '';
});

function setNumber(key: 'intervalMinutes' | 'minChatActivity', value: string | number): void {
  draft.value[key] = Number(value);
}

function currentJson(): string {
  return writeTimerPayload(draft.value);
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
      data-timer-payload-editor
      :show-close-button="false"
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 sm:max-w-xl"
    >
      <div class="flex flex-none items-start justify-between gap-4 px-6 pt-6 pb-4">
        <div class="grid gap-1.5">
          <DialogTitle class="text-lg font-semibold">{{ title }}</DialogTitle>
          <DialogDescription class="text-sm text-muted-foreground">
            {{ $t("dashboard.admin.content.timerEditor.description") }}
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
        <Field>
          <FieldLabel for="timer-name">{{ $t("dashboard.timers.editor.name") }}</FieldLabel>
          <Input
            id="timer-name"
            v-model="draft.name"
            autocomplete="off"
            :aria-label="$t('dashboard.timers.editor.name')"
          />
        </Field>

        <TemplateField
          id="timer-messages"
          v-model="draft.messages"
          context="timer"
          :rows="4"
          :panel="false"
          :label="$t('dashboard.timers.editor.messages')"
          :help="$t('dashboard.timers.editor.messagesHelp')"
          :placeholder="$t('dashboard.timers.editor.messagesPlaceholder')"
        />

        <div class="grid gap-4 md:grid-cols-2">
          <Field>
            <div class="flex items-center gap-1.5">
              <FieldLabel for="timer-interval">{{
                $t("dashboard.timers.editor.interval")
              }}</FieldLabel>
              <FieldInfo>{{
                $t("dashboard.timers.editor.intervalHelp", { minutes: defaultMinutes })
              }}</FieldInfo>
            </div>
            <Input
              id="timer-interval"
              type="number"
              :min="timerLimits.minMinutes"
              :max="timerLimits.maxMinutes"
              step="1"
              :aria-invalid="problem === 'interval'"
              :aria-label="$t('dashboard.timers.editor.interval')"
              :model-value="draft.intervalMinutes"
              @update:model-value="setNumber('intervalMinutes', $event)"
            />
          </Field>

          <Field>
            <div class="flex items-center gap-1.5">
              <FieldLabel for="timer-activity">{{
                $t("dashboard.timers.editor.activity")
              }}</FieldLabel>
              <FieldInfo>{{
                $t("dashboard.timers.editor.activityHelp", { lines: defaultChatLines })
              }}</FieldInfo>
            </div>
            <Input
              id="timer-activity"
              type="number"
              min="0"
              :max="timerLimits.maxActivity"
              step="1"
              :aria-invalid="problem === 'activity'"
              :aria-label="$t('dashboard.timers.editor.activity')"
              :model-value="draft.minChatActivity"
              @update:model-value="setNumber('minChatActivity', $event)"
            />
          </Field>
        </div>

        <div class="flex flex-wrap gap-x-8 gap-y-3">
          <LabeledSwitch
            v-model="draft.fireOnce"
            :label="$t('dashboard.timers.editor.once')"
            :help="$t('dashboard.timers.editor.onceHelp')"
          />
          <LabeledSwitch
            v-model="draft.isEnabled"
            :label="$t('dashboard.timers.editor.enabled')"
            :help="$t('dashboard.timers.editor.enabledHelp')"
          />
        </div>

        <InlineNotice v-if="problemText" data-timer-problem tone="error" compact>
          {{ problemText }}
        </InlineNotice>
      </div>
      <p
        v-if="message"
        data-timer-message
        class="flex-none px-6 pb-4 text-sm text-muted-foreground"
        role="status"
      >
        {{ message }}
      </p>
      <DialogFooter class="flex-none border-t border-border px-6 py-4">
        <Button
          data-timer-edit-json
          type="button"
          variant="outline"
          class="sm:mr-auto"
          @click="emit('editJson', currentJson())"
        >
          {{ $t("dashboard.admin.content.timerEditor.editJson") }}
        </Button>
        <Button
          data-timer-save
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
