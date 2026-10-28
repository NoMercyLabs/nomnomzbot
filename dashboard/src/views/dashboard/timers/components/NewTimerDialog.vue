<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A new timer: a name, what it says and how often. The other options start at the bot's defaults and
// change in the editor.
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useCreateTimer } from '@/api/timers';
import FormDialog from '@/components/layout/FormDialog.vue';
import ReplyModeField from '@/components/layout/ReplyModeField.vue';
import type { ReplyMode } from '@/components/layout/replyMode';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { targetToWire, type PickedTarget } from '@/lib/bindingTarget';
import { errorText, fieldErrorLines } from '@/lib/errors';

import { defaultMinutes, messagesOf, timerLimits } from './timerDraft';

const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const create = useCreateTimer(channelId);

const name = ref('');
const messages = ref('');
const minutes = ref(defaultMinutes);
const target = ref<PickedTarget | null>(null);
const mode = ref<ReplyMode>('simple');
const error = ref<string | undefined>(undefined);

watch(open, (isOpen: boolean): void => {
  if (!isOpen) return;

  name.value = '';
  messages.value = '';
  minutes.value = defaultMinutes;
  target.value = null;
  mode.value = 'simple';
  error.value = undefined;
});

async function submit(): Promise<void> {
  const simple = mode.value === 'simple';
  const lines = simple
    ? messagesOf(messages.value)
    : [];

  if (name.value.trim() === '') {
    error.value = t('dashboard.timers.dialog.nameRequired');

    return;
  }

  if (simple && lines.length === 0) {
    error.value = t('dashboard.timers.dialog.messageRequired');

    return;
  }

  if (!simple && target.value === null) {
    error.value = t('dashboard.timers.dialog.targetRequired');

    return;
  }

  if (
    !Number.isInteger(minutes.value) ||
    minutes.value < timerLimits.minMinutes ||
    minutes.value > timerLimits.maxMinutes
  ) {
    error.value = t('dashboard.timers.editor.intervalProblem', timerLimits);

    return;
  }

  error.value = undefined;

  try {
    await create.mutateAsync({
      name: name.value.trim(),
      messages: lines,
      intervalMinutes: minutes.value,
      ...(!simple && target.value
        ? { target: targetToWire(target.value) }
        : {}),
    });
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
    :title="$t('dashboard.timers.dialog.title')"
    :description="$t('dashboard.timers.dialog.description')"
    :action="$t('dashboard.timers.dialog.create')"
    :pending="create.isPending.value"
    :error="error"
    @submit="submit"
  >
    <Field>
      <FieldLabel for="new-timer-name">{{ $t("dashboard.timers.editor.name") }}</FieldLabel>
      <Input
        id="new-timer-name"
        v-model="name"
        autocomplete="off"
        :aria-label="$t('dashboard.timers.editor.name')"
        :placeholder="$t('dashboard.timers.dialog.namePlaceholder')"
      />
    </Field>
    <ReplyModeField
      v-model:mode="mode"
      v-model:target="target"
      :kinds="['command', 'pipeline', 'action']"
      :new-pipeline-name="name"
    >
      <TemplateField
        id="new-timer-messages"
        v-model="messages"
        context="timer"
        :rows="3"
        :label="$t('dashboard.timers.editor.messages')"
        :placeholder="$t('dashboard.timers.editor.messagesPlaceholder')"
      />
    </ReplyModeField>
    <Field>
      <FieldLabel for="new-timer-interval">{{ $t("dashboard.timers.editor.interval") }}</FieldLabel>
      <Input
        id="new-timer-interval"
        v-model.number="minutes"
        type="number"
        :min="timerLimits.minMinutes"
        :max="timerLimits.maxMinutes"
        step="1"
        :aria-label="$t('dashboard.timers.editor.interval')"
      />
      <FieldInfo>{{
        $t("dashboard.timers.editor.intervalHelp", { minutes: defaultMinutes })
      }}</FieldInfo>
    </Field>
  </FormDialog>
</template>
