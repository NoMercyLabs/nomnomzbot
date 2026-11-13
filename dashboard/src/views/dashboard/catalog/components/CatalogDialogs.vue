<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The destructive confirm dialog and the form dialog, each opened from one outline button.
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Button } from '@/components/ui/button';
import { Field, FieldError, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';

const { t } = useI18n();

const confirmOpen = ref(false);
const confirmPending = ref(false);
const confirmed = ref(0);
const consequences = ['chat', 'pipeline', 'history'].map((key) =>
  t(`dashboard.catalog.confirm.${key}`),
);

function confirmDelete(): void {
  confirmPending.value = true;

  setTimeout(() => {
    confirmPending.value = false;
    confirmOpen.value = false;
    confirmed.value += 1;
  }, 800);
}

const formOpen = ref(false);
const formPending = ref(false);
const formName = ref('');
const formNameError = ref<string | undefined>(undefined);
const formServerError = ref<string | undefined>(undefined);
const formCreated = ref<string[]>([]);

function submitForm(): void {
  formServerError.value = undefined;

  formNameError.value =
    formName.value.trim() === ''
      ? t('dashboard.catalog.form.nameMissing')
      : undefined;

  if (formNameError.value) return;

  formPending.value = true;

  setTimeout(() => {
    formPending.value = false;

    if (formName.value.trim() === 'so') {
      formServerError.value = t('dashboard.catalog.form.nameTaken');

      return;
    }

    formCreated.value.push(formName.value.trim());
    formName.value = '';
    formOpen.value = false;
  }, 800);
}
</script>

<template>
  <section data-catalog="dialogs" class="grid gap-4">
    <div class="flex items-center gap-4">
      <Button variant="outline" @click="confirmOpen = true">{{
        t('dashboard.catalog.confirm.open')
      }}</Button>
      <span class="text-sm text-muted-foreground">
        {{ t('dashboard.catalog.confirm.count', { count: confirmed }) }}
      </span>
    </div>
    <ConfirmDialog
      v-model:open="confirmOpen"
      :title="t('dashboard.catalog.confirm.title')"
      :description="t('dashboard.catalog.confirm.description')"
      :consequences="consequences"
      :action="t('dashboard.catalog.confirm.action')"
      :pending="confirmPending"
      @confirm="confirmDelete"
    />

    <div class="flex items-center gap-4">
      <Button variant="outline" @click="formOpen = true">{{
        t('dashboard.catalog.form.open')
      }}</Button>
      <span class="text-sm text-muted-foreground">
        {{ t('dashboard.catalog.form.count', { count: formCreated.length }) }}
      </span>
    </div>
    <FormDialog
      v-model:open="formOpen"
      :title="t('dashboard.catalog.form.title')"
      :description="t('dashboard.catalog.form.description')"
      :action="t('dashboard.catalog.form.action')"
      :pending="formPending"
      :error="formServerError"
      @submit="submitForm"
    >
      <Field :data-invalid="formNameError ? true : undefined">
        <FieldLabel for="catalog-command-name">{{
          t('dashboard.catalog.form.name')
        }}</FieldLabel>
        <Input
          id="catalog-command-name"
          v-model="formName"
          :aria-invalid="formNameError ? true : undefined"
          :placeholder="t('dashboard.catalog.form.namePlaceholder')"
        />
        <FieldError v-if="formNameError">{{ formNameError }}</FieldError>
      </Field>
    </FormDialog>
  </section>
</template>
