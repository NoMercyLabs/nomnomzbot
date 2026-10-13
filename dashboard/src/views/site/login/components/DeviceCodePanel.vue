<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { useMutation, useQuery } from '@tanstack/vue-query'
import { computed, onMounted, watch, type ComputedRef } from 'vue'
import { useRouter } from 'vue-router'

import {
  deviceApprovalQuery,
  startDeviceSignIn,
  type DeviceCode,
  type DeviceOutcome,
  type LoginProvider,
} from '@/api/signIn'
import { Button } from '@/components/ui/button'

const props = defineProps<{ provider: LoginProvider; returnTo: string }>()
const emit = defineEmits<{ cancel: [] }>()

const router = useRouter()
const start = useMutation({ mutationFn: () => startDeviceSignIn(props.provider.key) })
const code: ComputedRef<DeviceCode | null> = computed(() => start.data.value ?? null)
const site = computed(() => (code.value ? new URL(code.value.verificationUri).host : ''))

const approval = useQuery(computed(() => deviceApprovalQuery(props.provider.key, code.value)))

const outcome: ComputedRef<DeviceOutcome | 'failed' | null> = computed(() => {
  if (start.isError.value || (start.isSuccess.value && !code.value)) return 'failed'
  if (approval.isError.value) return 'error'
  const status: string | undefined = approval.data.value?.status

  return status === 'authorized' ||
    status === 'expired' ||
    status === 'denied' ||
    status === 'error'
    ? status
    : null
})

watch(outcome, async (now) => {
  if (now === 'authorized') await router.replace(props.returnTo)
})

onMounted(() => start.mutate())
</script>

<template>
  <div class="flex flex-col gap-3">
    <template v-if="outcome === null || outcome === 'authorized'">
      <p v-if="!code" class="text-sm text-muted-foreground" role="status">
        {{ $t('site.login.device.starting') }}
      </p>
      <template v-else>
        <p class="text-sm">{{ $t('site.login.device.instruction', { site }) }}</p>
        <p
          class="rounded-[12px] border border-border bg-muted py-3 text-center font-mono text-2xl font-semibold tracking-widest select-all"
          :aria-label="$t('site.login.device.codeLabel')"
        >
          {{ code.userCode }}
        </p>
        <Button as="a" :href="code.verificationUri" target="_blank" rel="noopener" class="w-full">
          {{ $t('site.login.device.open', { site }) }}
        </Button>
        <p class="text-sm text-muted-foreground" role="status">
          {{ $t('site.login.device.waiting') }}
        </p>
      </template>
    </template>
    <template v-else>
      <p class="text-sm" role="alert">
        {{ $t(`site.login.device.${outcome}`, { provider: provider.displayName }) }}
      </p>
      <Button variant="outline" class="w-full" @click="start.mutate()">{{
        $t('site.login.device.again')
      }}</Button>
    </template>
    <Button variant="ghost" class="w-full" @click="emit('cancel')">{{
      $t('site.login.device.back')
    }}</Button>
  </div>
</template>
