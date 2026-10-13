<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The sample rows the home page shows in its demo and in its feature blocks: chat lines,
// moderation queue rows and alerts. They are fixed sample words, never live data.
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'

import RowGroup from '@/components/layout/RowGroup.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'

type Kind = 'requests' | 'commands' | 'queue' | 'alerts' | 'sub'
type Row = { title: string; detail: string; action?: string; harm?: boolean }

const props = defineProps<{ kind: Kind }>()
const { t } = useI18n()

function line(user: string, text: string): Row {
  return { title: t(`site.home.chatLines.${user}`), detail: t(`site.home.chatLines.${text}`) }
}
function bot(reply: string): Row {
  return { title: t('site.home.chatLines.botName'), detail: t(`site.home.chatLines.${reply}`) }
}
function queued(n: 1 | 2 | 3): Row {
  return {
    title: t(`site.home.queue.who${n}`),
    detail: t(`site.home.queue.reason${n}`),
    action: t(`site.home.queue.action${n}`),
    harm: n === 1,
  }
}
function alert(which: 'raid' | 'sub'): Row {
  return { title: t(`site.home.alerts.${which}Title`), detail: t(`site.home.alerts.${which}Detail`) }
}

const rows: ComputedRef<Row[]> = computed(() => {
  switch (props.kind) {
    case 'requests':
      return [line('songrequestUser', 'songrequestText'), bot('songrequestReply'), line('raidUser', 'raidText')]
    case 'commands':
      return [
        line('socialsUser', 'socialsText'),
        bot('socialsReply'),
        line('uptimeUser', 'uptimeText'),
        bot('uptimeReply'),
      ]
    case 'queue':
      return [queued(1), queued(2), queued(3)]
    case 'alerts':
      return [alert('raid'), alert('sub')]
    default:
      return [alert('sub')]
  }
})
</script>

<template>
  <RowGroup>
    <SummaryRow v-for="row in rows" :key="row.title + row.detail" :title="row.title" :detail="row.detail">
      <template v-if="row.action" #end>
        <span class="text-xs font-medium" :class="row.harm ? 'text-destructive' : 'text-muted-foreground'">
          {{ row.action }}
        </span>
      </template>
    </SummaryRow>
  </RowGroup>
</template>
