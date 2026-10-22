<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The code script a command runs: one picker of the channel's scripts, the chosen script's description under it,
// and one link to the script page (to open the chosen script, or to make the first one when the channel has none).
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useCodeScripts, type CodeScriptSummary } from '@/api/codeScripts';
import OptionSelect from '@/components/layout/OptionSelect.vue';
import { Field, FieldLabel } from '@/components/ui/field';
import { errorText } from '@/lib/errors';

type Choice = { value: string; label: string };

const scriptId = defineModel<string>({ default: '' });

const { t } = useI18n();
const scripts = useCodeScripts();

const choices = computed<Choice[]>(() =>
  (scripts.data.value ?? []).map((script) => ({
    value: script.id,
    label: script.isEnabled
      ? script.name
      : t('dashboard.commands.editor.scriptOff', { name: script.name }),
  }) as const satisfies Choice));
const chosen = computed<CodeScriptSummary | undefined>(() =>
  (scripts.data.value ?? []).find((script): boolean => script.id === scriptId.value));
const empty = computed<boolean>(() => scripts.isSuccess.value && choices.value.length === 0);
</script>

<template>
  <Field data-code-script class="content-start">
    <FieldLabel>{{ $t('dashboard.commands.editor.script') }}</FieldLabel>
    <p v-if="scripts.isError.value" role="alert" class="text-sm text-destructive">
      {{
        $t('dashboard.commands.editor.scriptError', { message: errorText(scripts.error.value, $t) })
      }}
    </p>
    <p v-else-if="empty" data-code-script-empty class="text-sm text-muted-foreground">
      {{ $t('dashboard.commands.editor.scriptNone') }}
      <RouterLink :to="{ name: 'dashboard-scripts' }" class="underline underline-offset-2 hover:text-foreground">
        {{ $t('dashboard.commands.editor.scriptCreate') }}
      </RouterLink>
    </p>
    <template v-else>
      <OptionSelect
        v-model="scriptId"
        data-code-script-select
        :options="choices"
        :placeholder="$t('dashboard.commands.editor.scriptPlaceholder')"
        :aria-label="$t('dashboard.commands.editor.script')"
      />
      <p v-if="chosen?.description" class="text-xs text-muted-foreground">{{ chosen.description }}</p>
      <RouterLink
        v-if="chosen"
        data-code-script-link
        :to="{ name: 'dashboard-scripts', query: { script: chosen.id } }"
        class="w-fit text-sm text-muted-foreground underline underline-offset-2 hover:text-foreground"
      >
        {{ $t('dashboard.commands.editor.scriptOpen') }}
      </RouterLink>
    </template>
  </Field>
</template>
