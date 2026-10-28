<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// A step that asks for an app's client id and secret. The server names the fields; the page draws them.
import { reactive } from 'vue';

import { useSaveCredentials, useUseSharedTwitchApp, type SetupStep } from '@/api/setup';
import { Button } from '@/components/ui/button';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';

import InstructionLine from './InstructionLine.vue';

const props = defineProps<{ step: SetupStep }>();

const values = reactive<Record<string, string>>({});
const save = useSaveCredentials(() => props.step.key);
const useShared = useUseSharedTwitchApp();

const canSave = () =>
  props.step.fields.every((field) => !field.required || (values[field.key] ?? '').trim() !== '');
</script>

<template>
  <div class="flex flex-col gap-4">
    <ol class="flex list-decimal flex-col gap-1 pl-5 text-sm text-muted-foreground">
      <li v-for="line in step.instructions" :key="line"><InstructionLine :line="line" /></li>
    </ol>

    <form class="flex flex-col gap-3" @submit.prevent="save.mutate({ ...values })">
      <Field v-for="field in step.fields" :key="field.key">
        <div class="flex items-center gap-1.5">
          <FieldLabel :for="`setup-${step.key}-${field.key}`">{{ field.label }}</FieldLabel>
          <FieldInfo v-if="field.help">{{ field.help }}</FieldInfo>
        </div>
        <Input
          :id="`setup-${step.key}-${field.key}`"
          v-model="values[field.key]"
          :type="field.type"
          autocomplete="off"
        />
      </Field>

      <div class="flex flex-wrap items-center gap-2">
        <Button
          type="submit"
          :variant="step.complete ? 'outline' : 'default'"
          :disabled="!canSave() || save.isPending.value"
        >
          {{ $t('site.setup.saveCredentials') }}
        </Button>
        <Button
          v-if="step.useSharedAction"
          type="button"
          variant="outline"
          :disabled="useShared.isPending.value"
          @click="useShared.mutate()"
        >
          {{ $t('site.setup.useShared') }}
        </Button>
      </div>
      <p v-if="save.isError.value || useShared.isError.value" class="text-sm text-destructive" role="alert">
        {{ $t('site.setup.saveFailed') }}
      </p>
    </form>
  </div>
</template>
