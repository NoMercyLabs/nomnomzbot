<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A new savings jar: a name, an optional line about it, and an optional goal. It starts open.
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useCreateSavingsJar } from '@/api/economy';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

const open = defineModel<boolean>('open', { required: true });

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const create = useCreateSavingsJar(channelId);

const name = ref('');
const description = ref('');
const goal = ref<number | ''>('');
const iconUrl = ref('');
const maxWithdrawal = ref<number | ''>('');
const error = ref<string | undefined>(undefined);

watch(open, (isOpen: boolean): void => {
  if (!isOpen) return;

  name.value = '';
  description.value = '';
  goal.value = '';
  iconUrl.value = '';
  maxWithdrawal.value = '';
  error.value = undefined;
});

async function submit(): Promise<void> {
  if (name.value.trim() === '') {
    error.value = t('dashboard.economy.jars.dialog.nameRequired');

    return;
  }

  error.value = undefined;

  try {
    await create.mutateAsync({
      name: name.value.trim(),
      description: description.value.trim() === ''
        ? null
        : description.value.trim(),
      goalAmount: goal.value === ''
        ? null
        : goal.value,
      iconUrl: iconUrl.value.trim() === ''
        ? null
        : iconUrl.value.trim(),
      maxWithdrawalPerChannel: maxWithdrawal.value === ''
        ? null
        : maxWithdrawal.value,
      isOpen: true,
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
    :title="$t('dashboard.economy.jars.dialog.title')"
    :description="$t('dashboard.economy.jars.dialog.description')"
    :action="$t('dashboard.economy.jars.dialog.create')"
    :pending="create.isPending.value"
    :error="error"
    @submit="submit"
  >
    <Field>
      <FieldLabel for="jar-name">{{ $t('dashboard.economy.jars.dialog.name') }}</FieldLabel>
      <Input
        id="jar-name"
        v-model="name"
        autocomplete="off"
        :aria-label="$t('dashboard.economy.jars.dialog.name')"
      />
    </Field>
    <Field>
      <FieldLabel for="jar-description">{{ $t('dashboard.economy.jars.dialog.about') }}</FieldLabel>
      <Input
        id="jar-description"
        v-model="description"
        autocomplete="off"
        :aria-label="$t('dashboard.economy.jars.dialog.about')"
      />
    </Field>
    <Field>
      <FieldLabel for="jar-goal">{{ $t('dashboard.economy.jars.dialog.goal') }}</FieldLabel>
      <Input
        id="jar-goal"
        v-model.number="goal"
        type="number"
        min="1"
        step="1"
        :aria-label="$t('dashboard.economy.jars.dialog.goal')"
      />
    </Field>
    <Field>
      <FieldLabel for="jar-icon">{{ $t('dashboard.economy.jars.dialog.icon') }}</FieldLabel>
      <Input
        id="jar-icon"
        v-model="iconUrl"
        type="url"
        autocomplete="off"
        :aria-label="$t('dashboard.economy.jars.dialog.icon')"
      />
    </Field>
    <Field>
      <div class="flex items-center gap-1.5">
        <FieldLabel for="jar-max-withdrawal">{{
          $t('dashboard.economy.jars.dialog.maxWithdrawal')
        }}</FieldLabel>
        <FieldInfo>{{ $t('dashboard.economy.jars.dialog.maxWithdrawalHelp') }}</FieldInfo>
      </div>
      <Input
        id="jar-max-withdrawal"
        v-model.number="maxWithdrawal"
        type="number"
        min="1"
        step="1"
        :aria-label="$t('dashboard.economy.jars.dialog.maxWithdrawal')"
      />
    </Field>
  </FormDialog>
</template>
