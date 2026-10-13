<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { BookOpenText, ChevronsUpDown, LogOut, Shield } from '@lucide/vue'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter, type RouteLocationRaw, type Router } from 'vue-router'

import { signOut, useCurrentUser } from '@/api/auth'
import LanguagePicker from '@/components/layout/LanguagePicker.vue'
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'
import { Separator } from '@/components/ui/separator'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { loginRoute } from '@/views/site/login/route'

// icon: only the picture, for the narrow header.
defineProps<{ icon?: boolean }>()

const { t } = useI18n()
const router: Router = useRouter()
const { data: user } = useCurrentUser()
const { current } = useCurrentChannel()
const open = ref(false)

const settingsLink: RouteLocationRaw = { name: 'dashboard-settings', query: { view: 'device' } }
const adminLink: RouteLocationRaw = { name: 'dashboard-admin-channels' }

// The two links appear when their page exists, like every other entry of the frame.
const hasReadability = computed<boolean>(() => router.hasRoute('dashboard-settings'))
const hasAdmin = computed<boolean>(() => user.value?.isAdmin === true && router.hasRoute('dashboard-admin-channels'))

const initials = computed<string>(() => (user.value?.displayName ?? '').replace(/[^A-Za-z0-9]/g, '').slice(0, 2).toUpperCase())
const role = computed<string>(() => (current.value?.kind === 'moderated' ? t('frame.profile.moderator') : t('frame.profile.broadcaster')))

async function leave(): Promise<void> {
  open.value = false
  await signOut()
  await router.push({ name: loginRoute.name })
}
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <button
        type="button"
        data-slot="profile-menu"
        :aria-label="t('frame.profile.menu')"
        class="flex min-h-11 min-w-0 items-center gap-2 rounded-md p-2 text-left outline-none hover:bg-sidebar-accent focus-visible:ring-2 focus-visible:ring-ring"
        :class="icon ? '' : 'w-full'"
      >
        <Avatar class="size-8 rounded-lg">
          <AvatarImage v-if="user?.profileImageUrl" :src="user.profileImageUrl" :alt="''" />
          <AvatarFallback class="rounded-lg">{{ initials }}</AvatarFallback>
        </Avatar>
        <template v-if="!icon">
          <span class="min-w-0 flex-1">
            <span class="block truncate text-sm font-medium">{{ user?.displayName }}</span>
            <span class="block truncate text-xs text-muted-foreground">{{ role }}</span>
          </span>
          <ChevronsUpDown class="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
        </template>
      </button>
    </PopoverTrigger>
    <PopoverContent align="end" side="top" class="w-64 p-1">
      <p class="truncate px-3 py-2 text-sm font-medium">{{ user?.displayName }}</p>
      <Separator />
      <div class="flex flex-col gap-1 py-1">
        <Button v-if="hasReadability" variant="ghost" class="justify-start" as-child>
          <RouterLink :to="settingsLink" @click="open = false">
            <BookOpenText aria-hidden="true" />
            {{ t('frame.profile.readability') }}
          </RouterLink>
        </Button>
        <div class="flex items-center justify-between gap-2 px-3 py-1">
          <span class="text-sm">{{ t('common.language') }}</span>
          <LanguagePicker />
        </div>
        <Button v-if="hasAdmin" variant="ghost" class="justify-start" as-child>
          <RouterLink :to="adminLink" @click="open = false">
            <Shield aria-hidden="true" />
            {{ t('frame.profile.admin') }}
          </RouterLink>
        </Button>
      </div>
      <Separator />
      <Button variant="ghost" class="mt-1 w-full justify-start" @click="leave">
        <LogOut aria-hidden="true" />
        {{ t('common.signOut') }}
      </Button>
    </PopoverContent>
  </Popover>
</template>
