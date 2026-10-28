<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The replies of a random-reply command, edited in a dialog so the editor never changes height.
// The dialog edits a copy: Use these replies puts it in the command draft, Cancel drops it.
// Another command's list can be the start; a chosen list is copied, never shared.
import { ref, watch } from 'vue';

import FormDialog from '@/components/layout/FormDialog.vue';
import { Button } from '@/components/ui/button';

import RandomReplyList from './RandomReplyList.vue';
import type { ReplySet } from './replySet';

const open = defineModel<boolean>('open', { required: true });
const replies = defineModel<string[]>({ required: true });

defineProps<{ sets: ReplySet[] }>();

const copy = ref<string[]>([]);

watch(open, (isOpen: boolean): void => {
  if (isOpen) {
    copy.value = [...replies.value];
  }
}, { immediate: true });

function choose(set: ReplySet): void {
  copy.value = [...set.replies];
}

function apply(): void {
  replies.value = [...copy.value];
  open.value = false;
}
</script>

<template>
  <FormDialog
    v-model:open="open"
    data-random-panel
    :title="$t('dashboard.commands.editor.randomPanel')"
    :description="$t('dashboard.commands.editor.randomHelp')"
    :action="$t('dashboard.commands.editor.useReplies')"
    class="sm:max-w-2xl"
    @submit="apply"
  >
    <div class="grid gap-4">
      <RandomReplyList v-model="copy" />
      <div v-if="sets.length > 0" data-reply-sets class="grid gap-2 border-t border-border pt-4">
        <h4 class="text-sm font-medium">{{ $t("dashboard.commands.editor.chooseSet") }}</h4>
        <div class="flex flex-wrap gap-2">
          <Button
            v-for="set in sets"
            :key="set.name"
            type="button"
            variant="outline"
            size="sm"
            @click="choose(set)"
          >
            {{ $t("dashboard.commands.editor.useSet", { name: set.name }) }}
          </Button>
        </div>
      </div>
    </div>
  </FormDialog>
</template>
