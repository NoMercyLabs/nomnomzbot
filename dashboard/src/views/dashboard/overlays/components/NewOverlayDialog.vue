<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A new overlay: a name and the language its source is written in. The code, settings and events
// start empty and change in the editor.
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useCreateWidget } from '@/api/widgets';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { RadioGroup, RadioGroupItem } from '@/components/ui/radio-group';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { errorText, fieldErrorLines } from '@/lib/errors';

const FRAMEWORKS = ['vue', 'vanilla'] as const;

type Framework = (typeof FRAMEWORKS)[number];

const open = defineModel<boolean>('open', { required: true });
const emit = defineEmits<{ created: [id: string | null] }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const create = useCreateWidget(channelId);

const name = ref('');
const framework = ref<Framework>('vue');
const error = ref<string | undefined>(undefined);

watch(open, (isOpen: boolean): void => {
  if (!isOpen) return;

  name.value = '';
  framework.value = 'vue';
  error.value = undefined;
});

async function submit(): Promise<void> {
  if (name.value.trim() === '') {
    error.value = t('dashboard.overlays.create.nameRequired');

    return;
  }

  error.value = undefined;

  let id: string | null;

  try {
    id = await create.mutateAsync({ name: name.value.trim(), framework: framework.value });
  } catch (failure) {
    error.value = [errorText(failure, t), ...fieldErrorLines(failure)].join(' ');

    return;
  }

  open.value = false;
  emit('created', id);
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    :title="$t('dashboard.overlays.create.title')"
    :description="$t('dashboard.overlays.create.description')"
    :action="$t('dashboard.overlays.create.action')"
    :pending="create.isPending.value"
    :error="error"
    @submit="submit"
  >
    <Field>
      <FieldLabel for="new-overlay-name">{{ $t("dashboard.overlays.create.name") }}</FieldLabel>
      <Input
        id="new-overlay-name"
        v-model="name"
        autocomplete="off"
        :aria-label="$t('dashboard.overlays.create.name')"
        :placeholder="$t('dashboard.overlays.create.namePlaceholder')"
      />
    </Field>
    <Field>
      <FieldLabel>{{ $t("dashboard.overlays.create.framework") }}</FieldLabel>
      <RadioGroup v-model="framework" class="grid gap-2">
        <div v-for="value in FRAMEWORKS" :key="value" class="flex items-center gap-2">
          <RadioGroupItem :id="`new-overlay-${value}`" :value="value" />
          <Label :for="`new-overlay-${value}`" class="font-normal">
            {{ $t(`dashboard.overlays.create.frameworks.${value}`) }}
          </Label>
        </div>
      </RadioGroup>
      <FieldInfo>{{ $t("dashboard.overlays.create.frameworkHelp") }}</FieldInfo>
    </Field>
  </FormDialog>
</template>
