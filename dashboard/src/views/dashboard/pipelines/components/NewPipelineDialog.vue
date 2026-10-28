<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The New pipeline dialog: one name field. The bot makes an empty pipeline; the page then opens its workbench.
// The dialog stays open on a refusal and shows the bot's reason.
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useCreatePipeline } from '@/api/pipelines';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

const { t } = useI18n();
const open = defineModel<boolean>('open', { required: true });
const emit = defineEmits<{ created: [id: string] }>();

const { channelId } = useCurrentChannel();
const create = useCreatePipeline(channelId);
const name = ref('');
const error = ref<string | undefined>(undefined);

watch(open, (isOpen: boolean) => {
  if (!isOpen) return;

  name.value = '';
  error.value = undefined;
});

async function submit(): Promise<void> {
  const trimmed = name.value.trim();

  if (trimmed === '') return;

  error.value = undefined;

  try {
    const created = await create.mutateAsync({ name: trimmed });

    open.value = false;
    emit('created', created.id);
  } catch (failure) {
    error.value = [errorText(failure, t), ...fieldErrorLines(failure)].join(' ');
  }
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-pipelines-new-dialog
    :title="$t('dashboard.pipelines.newDialog.title')"
    :description="$t('dashboard.pipelines.newDialog.description')"
    :action="$t('dashboard.pipelines.newDialog.action')"
    :pending="create.isPending.value"
    :error="error"
    @submit="submit"
  >
    <Field>
      <FieldLabel for="pipeline-new-name">{{ $t('dashboard.pipelines.newDialog.name') }}</FieldLabel>
      <Input
        id="pipeline-new-name"
        v-model="name"
        data-pipelines-new-name
        autofocus
        :maxlength="100"
        :placeholder="$t('dashboard.pipelines.newDialog.namePlaceholder')"
      />
    </Field>
  </FormDialog>
</template>
