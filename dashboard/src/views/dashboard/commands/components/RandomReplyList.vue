<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The replies a command picks from at random. There is always at least one row.
import Plus from '~icons/nnz/add';
import X from '~icons/nnz/cross';

import { Button } from '@/components/ui/button';
import TemplateField from '@/components/layout/TemplateField.vue';

const replies = defineModel<string[]>({ required: true });

function set(index: number, text: string | number): void {
  replies.value = replies.value.map((reply, at): string =>
    at === index
      ? String(text)
      : reply,
  );
}

function add(): void {
  replies.value = [...replies.value, ''];
}

function remove(index: number): void {
  replies.value = replies.value.filter((_reply, at): boolean => at !== index);
}
</script>

<template>
  <div class="grid gap-2">
    <div
      v-for="(reply, index) in replies"
      :key="index"
      data-random-reply
      class="flex items-start gap-2"
    >
      <TemplateField
        class="min-w-0 flex-1"
        context="command"
        :model-value="reply"
        :maxlength="500"
        :label="$t('dashboard.commands.editor.replyNumber', { number: index + 1 })"
        @update:model-value="set(index, $event)"
      />
      <Button
        type="button"
        variant="ghost"
        size="icon-sm"
        class="mt-7"
        :disabled="replies.length === 1"
        :aria-label="$t('dashboard.commands.editor.removeReply', { number: index + 1 })"
        @click="remove(index)"
      >
        <X aria-hidden="true" />
      </Button>
    </div>
    <Button type="button"
            variant="outline"
            size="sm"
            class="justify-self-start"
            @click="add"
    >
      <Plus aria-hidden="true" />
      {{ $t('dashboard.commands.editor.addReply') }}
    </Button>
  </div>
</template>
