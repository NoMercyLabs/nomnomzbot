<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { Inbox } from '@lucide/vue'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'

import { useActionRequired } from '@/api/actionRequired'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { useSaveErrorsStore } from '@/stores/saveErrors'

const { t, te } = useI18n()
const { channelId } = useCurrentChannel()
const { data: items } = useActionRequired(channelId)
const saveErrors = useSaveErrorsStore()
const open = ref(false)
const count = computed(() => (items.value?.length ?? 0) + saveErrors.items.length)

// The server names each item by a words key; a key this client has no words for yet shows a plain fallback.
function words(key: string, fallback: string): string {
  return te(key) ? t(key) : t(fallback)
}

// A deep link is an in-app path. Anything else is not followed.
function inApp(link: string): boolean {
  return link.startsWith('/app')
}
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <Button variant="outline" size="sm" data-error-inbox :aria-label="t('frame.inbox.title')">
        <Inbox aria-hidden="true" />
        <Badge v-if="count" variant="secondary">{{ count }}</Badge>
      </Button>
    </PopoverTrigger>
    <PopoverContent align="end" class="w-80 p-0">
      <h2 class="border-b border-border px-4 py-3 text-sm font-semibold">
        {{ t('frame.inbox.title') }}
      </h2>
      <p v-if="!count" class="px-4 py-6 text-sm text-muted-foreground">
        {{ t('frame.inbox.empty') }}
      </p>
      <ul v-else class="flex max-h-80 flex-col overflow-y-auto p-1">
        <li
          v-for="failed in saveErrors.items"
          :key="failed.id"
          class="flex flex-col gap-1 rounded-md px-3 py-2"
        >
          <span class="text-sm font-medium">{{ t(failed.titleKey) }}</span>
          <span class="text-sm text-muted-foreground">{{ t(failed.messageKey) }}</span>
          <Button
            variant="ghost"
            size="sm"
            class="mt-1 self-start"
            @click="saveErrors.dismiss(failed.id)"
          >
            {{ t('frame.inbox.dismiss') }}
          </Button>
        </li>
        <li v-for="item in items" :key="item.id" class="flex flex-col gap-1 rounded-md px-3 py-2">
          <span class="text-sm font-medium">{{
            words(item.titleKey, 'frame.inbox.unknownTitle')
          }}</span>
          <span class="text-sm text-muted-foreground">{{
            words(item.messageKey, 'frame.inbox.unknownMessage')
          }}</span>
          <Button
            v-if="inApp(item.deepLinkRoute)"
            variant="outline"
            size="sm"
            class="mt-1 self-start"
            as-child
          >
            <RouterLink :to="item.deepLinkRoute" @click="open = false">{{
              t('frame.inbox.open')
            }}</RouterLink>
          </Button>
        </li>
      </ul>
    </PopoverContent>
  </Popover>
</template>
