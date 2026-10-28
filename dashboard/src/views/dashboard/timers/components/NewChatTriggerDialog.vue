<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A new chat trigger: the words to watch for and the reply. The other options start at the bot's
// defaults and change in the editor.
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useCreateChatTrigger } from '@/api/chatTriggers';
import FormDialog from '@/components/layout/FormDialog.vue';
import TemplateField from '@/components/layout/TemplateField.vue';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const create = useCreateChatTrigger(channelId);

const pattern = ref('');
const response = ref('');
const error = ref<string | undefined>(undefined);

watch(open, (isOpen: boolean): void => {
  if (!isOpen) return;

  pattern.value = '';
  response.value = '';
  error.value = undefined;
});

async function submit(): Promise<void> {
  if (pattern.value.trim() === '') {
    error.value = t('dashboard.timers.triggers.dialog.patternRequired');

    return;
  }

  if (response.value.trim() === '') {
    error.value = t('dashboard.timers.triggers.dialog.responseRequired');

    return;
  }

  error.value = undefined;

  try {
    await create.mutateAsync({ pattern: pattern.value.trim(), response: response.value.trim() });
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
    :title="$t('dashboard.timers.triggers.dialog.title')"
    :description="$t('dashboard.timers.triggers.dialog.description')"
    :action="$t('dashboard.timers.triggers.dialog.create')"
    :pending="create.isPending.value"
    :error="error"
    @submit="submit"
  >
    <Field>
      <FieldLabel for="new-trigger-pattern">{{ $t('dashboard.timers.triggers.editor.pattern') }}</FieldLabel>
      <Input
        id="new-trigger-pattern"
        v-model="pattern"
        autocomplete="off"
        :aria-label="$t('dashboard.timers.triggers.editor.pattern')"
      />
    </Field>
    <TemplateField
      id="new-trigger-response"
      v-model="response"
      context="command"
      :rows="3"
      :maxlength="500"
      :label="$t('dashboard.timers.triggers.editor.response')"
    />
  </FormDialog>
</template>
