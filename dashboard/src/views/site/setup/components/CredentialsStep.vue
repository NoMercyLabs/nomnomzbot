<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A step that asks for an app's client id and secret. The server names the fields; the page draws them.
import { reactive } from 'vue';

import { useSaveCredentials, useUseSharedTwitchApp, type SetupStep } from '@/api/setup';
import { Button } from '@/components/ui/button';
import { Field, FieldInfo, FieldLabel } from '@/components/ui/field';
import { Input } from '@/components/ui/input';

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
      <li v-for="line in step.instructions" :key="line">{{ line }}</li>
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
