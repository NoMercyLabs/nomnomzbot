<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A new overlay: a ready-made one from the gallery, or a blank one from a name and a code language.
// A gallery overlay keeps its gallery link, so its settings form comes with it.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import { useCreateWidget, useInstallWidget, useWidgetGallery } from '@/api/widgets';
import FormDialog from '@/components/layout/FormDialog.vue';
import { Field, FieldInfo, FieldLabel, FieldTitle } from '@/components/ui/field';
import { Input } from '@/components/ui/input';
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
const install = useInstallWidget(channelId);
const gallery = useWidgetGallery(open);

const BLANK = 'blank';

const start = ref<string>(BLANK);
const name = ref('');
const framework = ref<Framework>('vue');
const error = ref<string | undefined>(undefined);

const isBlank = computed(() => start.value === BLANK);
const pending = computed(() => create.isPending.value || install.isPending.value);

watch(open, (isOpen: boolean): void => {
  if (!isOpen) return;

  start.value = BLANK;
  name.value = '';
  framework.value = 'vue';
  error.value = undefined;
});

async function submit(): Promise<void> {
  if (isBlank.value) {
    await submitBlank();

    return;
  }

  error.value = undefined;

  let id: string | null;

  try {
    id = await install.mutateAsync(start.value);
  } catch (failure) {
    error.value = errorText(failure, t);

    return;
  }

  open.value = false;
  emit('created', id);
}

async function submitBlank(): Promise<void> {
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
    :action="isBlank ? $t('dashboard.overlays.create.action') : $t('dashboard.overlays.create.install')"
    :pending="pending"
    :error="error"
    @submit="submit"
  >
    <Field>
      <FieldTitle>{{ $t("dashboard.overlays.create.start") }}</FieldTitle>
      <RadioGroup v-model="start" class="grid max-h-64 gap-1 overflow-y-auto">
        <div class="flex items-start gap-2 rounded-md p-2">
          <RadioGroupItem :id="`new-overlay-${BLANK}`" :value="BLANK" class="mt-0.5" />
          <FieldLabel :for="`new-overlay-${BLANK}`" class="grid gap-0.5 font-normal">
            <span class="font-medium">{{ $t("dashboard.overlays.create.blank") }}</span>
            <span class="text-muted-foreground text-xs">{{ $t("dashboard.overlays.create.blankHelp") }}</span>
          </FieldLabel>
        </div>
        <div v-for="item in gallery.data.value ?? []" :key="item.id" class="flex items-start gap-2 rounded-md p-2">
          <RadioGroupItem :id="`new-overlay-gallery-${item.id}`" :value="item.id" class="mt-0.5" />
          <FieldLabel :for="`new-overlay-gallery-${item.id}`" class="grid gap-0.5 font-normal">
            <span class="font-medium">{{ item.name }}</span>
            <span v-if="item.description" class="text-muted-foreground text-xs">{{ item.description }}</span>
            <span v-if="item.trustTier !== 'first_party'" class="text-muted-foreground text-xs">
              {{ $t("dashboard.overlays.create.community") }}
            </span>
          </FieldLabel>
        </div>
      </RadioGroup>
      <FieldInfo v-if="gallery.isError.value">{{ $t("dashboard.overlays.create.galleryFailed") }}</FieldInfo>
    </Field>
    <Field v-if="isBlank">
      <FieldLabel for="new-overlay-name">{{ $t("dashboard.overlays.create.name") }}</FieldLabel>
      <Input
        id="new-overlay-name"
        v-model="name"
        autocomplete="off"
        :aria-label="$t('dashboard.overlays.create.name')"
        :placeholder="$t('dashboard.overlays.create.namePlaceholder')"
      />
    </Field>
    <Field v-if="isBlank">
      <FieldTitle>{{ $t("dashboard.overlays.create.framework") }}</FieldTitle>
      <RadioGroup v-model="framework" class="grid gap-2">
        <div v-for="value in FRAMEWORKS" :key="value" class="flex items-center gap-2">
          <RadioGroupItem :id="`new-overlay-${value}`" :value="value" />
          <FieldLabel :for="`new-overlay-${value}`" class="font-normal">
            {{ $t(`dashboard.overlays.create.frameworks.${value}`) }}
          </FieldLabel>
        </div>
      </RadioGroup>
      <FieldInfo>{{ $t("dashboard.overlays.create.frameworkHelp") }}</FieldInfo>
    </Field>
  </FormDialog>
</template>
