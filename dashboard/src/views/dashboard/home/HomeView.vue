<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { useRouter } from 'vue-router'

import { signOut, useCurrentUser } from '@/api/auth'
import { Button } from '@/components/ui/button'
import { loginRoute } from '@/views/site/login/route'

const router = useRouter()
const { data: user } = useCurrentUser()

async function leave(): Promise<void> {
  await signOut()
  await router.push({ name: loginRoute.name })
}
</script>

<template>
  <div class="flex items-center justify-between gap-4 p-6">
    <h1 class="text-xl font-semibold">
      {{ user ? $t('dashboard.home.greeting', { name: user.displayName }) : '' }}
    </h1>
    <Button variant="outline" @click="leave">{{ $t('common.signOut') }}</Button>
  </div>
</template>
