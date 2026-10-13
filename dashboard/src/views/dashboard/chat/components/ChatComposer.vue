<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The send box under the feed. Send is the one primary action of the page. A message over the
// limit never leaves the browser; a failed send keeps the text so nothing is lost.
import { computed, ref, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'

import { chatMessageMaxLength, useSendChat } from '@/api/chat'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Spinner } from '@/components/ui/spinner'

const COUNT_FROM: number = 400

const props = defineProps<{ channelId: string | null }>()

const { t } = useI18n()
const sender = useSendChat(() => props.channelId)

const text = ref('')
const failed = ref(false)

const tooLong = computed(() => text.value.length > chatMessageMaxLength)
const showCount = computed(() => text.value.length > COUNT_FROM)
const canSend = computed(
  () => text.value.trim() !== '' && !tooLong.value && !sender.isPending.value,
)
const problem: ComputedRef<string> = computed(() => {
  if (tooLong.value) return t('dashboard.chat.composer.tooLong', { max: chatMessageMaxLength })

  return failed.value ? t('dashboard.chat.composer.failed') : ''
})

async function submit(): Promise<void> {
  if (!canSend.value) return
  failed.value = false

  try {
    await sender.mutateAsync({ message: text.value, senderIdentity: 'you' })
    text.value = ''
  } catch {
    failed.value = true
  }
}
</script>

<template>
  <form class="flex w-full flex-col gap-2" @submit.prevent="submit">
    <div class="flex items-center gap-2">
      <Input
        v-model="text"
        data-composer
        autocomplete="off"
        :aria-label="$t('dashboard.chat.composer.label')"
        :aria-invalid="tooLong"
        :placeholder="$t('dashboard.chat.composer.placeholder')"
      />
      <span
        v-if="showCount"
        data-composer-count
        class="flex-none text-sm text-muted-foreground tabular-nums"
        :class="{ 'text-destructive': tooLong }"
      >
        {{ $t('dashboard.chat.composer.count', { count: text.length, max: chatMessageMaxLength }) }}
      </span>
      <Button type="submit" :disabled="!canSend">
        <Spinner v-if="sender.isPending.value" />
        {{ $t('dashboard.chat.composer.send') }}
      </Button>
    </div>
    <p v-if="problem" role="alert" class="text-sm text-destructive">{{ problem }}</p>
  </form>
</template>
