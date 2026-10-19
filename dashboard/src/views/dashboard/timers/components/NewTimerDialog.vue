<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A new timer: a name, what it says and how often. The other options start at the bot's defaults and
// change in the editor.
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useCreateTimer } from '@/api/timers';
import FormDialog from '@/components/layout/FormDialog.vue';
import TargetPicker from '@/components/layout/TargetPicker.vue';
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
const error = ref<string | undefined>(undefined);

watch(open, (isOpen: boolean): void => {
  if (!isOpen) return;

  name.value = '';
  messages.value = '';
  minutes.value = defaultMinutes;
  target.value = null;
  error.value = undefined;
});

async function submit(): Promise<void> {
  const lines = messagesOf(messages.value);

  if (name.value.trim() === '') {
    error.value = t('dashboard.timers.dialog.nameRequired');

    return;
  }

  if (lines.length === 0) {
    error.value = t('dashboard.timers.dialog.messageRequired');

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
      ...(target.value
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
    <TemplateField
      id="new-timer-messages"
      v-model="messages"
      context="timer"
      :rows="3"
      :label="$t('dashboard.timers.editor.messages')"
      :placeholder="$t('dashboard.timers.editor.messagesPlaceholder')"
    />
    <Field>
      <FieldLabel>{{ $t("dashboard.timers.editor.target") }}</FieldLabel>
      <TargetPicker
        v-model:target="target"
        clearable
        :kinds="['command', 'pipeline', 'action']"
        :label="$t('dashboard.timers.editor.target')"
      />
    </Field>
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
