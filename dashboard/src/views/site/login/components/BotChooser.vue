<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { Check, X } from '@lucide/vue'
import { ref, type Ref } from 'vue'

import { activeBot } from '@/api/activeBot'
import { switchBot } from '@/api/auth'
import { probeBot, type BotProbe } from '@/api/system'
import PanelCard from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { officialOrigin, servedOrigin, toBotOrigin } from '@/lib/botAddress'
import { useConnectionsStore } from '@/stores/connections'

const emit = defineEmits<{ back: [] }>()

const connections = useConnectionsStore()
const chosen: Ref<string> = ref(activeBot.value)
const typed: Ref<string> = ref('')
const checking: Ref<boolean> = ref(false)
const checked: Ref<{ origin: string; probe: BotProbe } | 'invalid' | null> = ref(null)

function host(origin: string): string {
  return new URL(origin).host
}

function detail(origin: string): string {
  if (origin === servedOrigin) return 'site.login.bots.thisPage'
  return origin === officialOrigin ? 'site.login.bots.official' : 'site.login.bots.used'
}

function forget(origin: string): void {
  connections.forget(origin)
  if (chosen.value === origin) chosen.value = activeBot.value === origin ? servedOrigin : activeBot.value
}

// This client stays open; only the bot it talks to changes.
function useChosen(): void {
  switchBot(chosen.value)
  emit('back')
}

async function check(): Promise<void> {
  const origin: string | null = toBotOrigin(typed.value)
  if (!origin) {
    checked.value = 'invalid'
    return
  }
  checking.value = true
  const probe: BotProbe = await probeBot(origin)
  checking.value = false
  checked.value = { origin, probe }
  if (probe.state === 'unreachable' || probe.state === 'mixedContent') return
  connections.remember(origin)
  chosen.value = origin
  typed.value = ''
}
</script>

<template>
  <PanelCard :title="$t('site.login.bots.title')" :description="$t('site.login.bots.description')" tier="lead" pad>
    <div class="flex flex-col gap-6">
      <RowGroup role="radiogroup" :aria-label="$t('site.login.bots.saved')">
        <SummaryRow
          v-for="origin in connections.bots"
          :key="origin"
          pick
          role="radio"
          :aria-checked="chosen === origin"
          :selected="chosen === origin"
          :title="host(origin)"
          :detail="$t(detail(origin))"
          @click="chosen = origin"
        >
          <template #end>
            <Check v-if="chosen === origin" class="size-4 flex-none" aria-hidden="true" />
          </template>
          <template v-if="!connections.isAlwaysListed(origin)" #trailing>
            <Button
              variant="ghost"
              size="icon-sm"
              class="mr-1"
              :aria-label="$t('site.login.bots.forget', { host: host(origin) })"
              @click="forget(origin)"
            >
              <X />
            </Button>
          </template>
        </SummaryRow>
      </RowGroup>

      <form class="flex flex-col gap-2" @submit.prevent="check">
        <Label for="bot-address">{{ $t('site.login.bots.address') }}</Label>
        <div class="flex gap-2">
          <Input
            id="bot-address"
            v-model="typed"
            :placeholder="$t('site.login.bots.placeholder')"
            autocomplete="url"
            spellcheck="false"
            :aria-invalid="checked === 'invalid' || undefined"
          />
          <Button type="submit" variant="outline" :disabled="checking || !typed.trim()">
            {{ $t('site.login.bots.check') }}
          </Button>
        </div>
        <p v-if="checking" class="text-sm text-muted-foreground" role="status">{{ $t('site.login.bots.checking') }}</p>
        <p v-else-if="checked === 'invalid'" class="text-sm text-destructive" role="alert">
          {{ $t('site.login.bots.invalid') }}
        </p>
        <p v-else-if="checked" class="flex items-baseline gap-2 text-sm" role="status">
          <span class="dot" :class="{ ok: checked.probe.state === 'ready', bad: checked.probe.state === 'unreachable' || checked.probe.state === 'mixedContent' }" />
          <span>{{ $t(`site.login.bots.${checked.probe.state}`, { host: host(checked.origin) }) }}</span>
        </p>
      </form>
    </div>
    <template #footer>
      <Button @click="useChosen">{{ $t('site.login.bots.use') }}</Button>
      <Button variant="ghost" @click="emit('back')">{{ $t('site.login.bots.back') }}</Button>
    </template>
  </PanelCard>
</template>
