<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The replies a command picks from at random. There is always at least one row.
import { Plus, X } from '@lucide/vue'

import { Button } from '@/components/ui/button'
import { Textarea } from '@/components/ui/textarea'

const replies = defineModel<string[]>({ required: true })

function set(index: number, text: string | number): void {
  replies.value = replies.value.map((reply: string, at: number): string => (at === index ? String(text) : reply))
}

function add(): void {
  replies.value = [...replies.value, '']
}

function remove(index: number): void {
  replies.value = replies.value.filter((_reply: string, at: number): boolean => at !== index)
}
</script>

<template>
  <div class="grid gap-2">
    <div v-for="(reply, index) in replies" :key="index" data-random-reply class="flex items-start gap-2">
      <Textarea
        :model-value="reply"
        :aria-label="$t('dashboard.commands.editor.replyNumber', { number: index + 1 })"
        @update:model-value="set(index, $event)"
      />
      <Button
        type="button"
        variant="ghost"
        size="icon-sm"
        :disabled="replies.length === 1"
        :aria-label="$t('dashboard.commands.editor.removeReply', { number: index + 1 })"
        @click="remove(index)"
      >
        <X aria-hidden="true" />
      </Button>
    </div>
    <Button type="button" variant="outline" size="sm" class="justify-self-start" @click="add">
      <Plus aria-hidden="true" />
      {{ $t('dashboard.commands.editor.addReply') }}
    </Button>
  </div>
</template>
