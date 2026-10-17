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
import TemplateField from '@/components/layout/TemplateField.vue';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

import { messagesOf } from './timerDraft';

const defaultMinutes = 15;

const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const create = useCreateTimer(channelId);

const name = ref('');
const messages = ref('');
const minutes = ref(defaultMinutes);
const error = ref<string | undefined>(undefined);

watch(open, (isOpen: boolean): void => {
  if (!isOpen) return;

  name.value = '';
  messages.value = '';
  minutes.value = defaultMinutes;
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

  error.value = undefined;

  try {
    await create.mutateAsync({
      name: name.value.trim(),
      messages: lines,
      intervalMinutes: minutes.value,
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
      <FieldLabel for="new-timer-name">{{ $t('dashboard.timers.editor.name') }}</FieldLabel>
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
    />
    <Field>
      <FieldLabel for="new-timer-interval">{{
        $t('dashboard.timers.editor.interval')
      }}</FieldLabel>
      <Input
        id="new-timer-interval"
        v-model.number="minutes"
        type="number"
        min="1"
        step="1"
        :aria-label="$t('dashboard.timers.editor.interval')"
      />
    </Field>
  </FormDialog>
</template>
