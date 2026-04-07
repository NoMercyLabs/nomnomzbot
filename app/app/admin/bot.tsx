import { View, Text, ScrollView, Pressable } from 'react-native'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { apiClient } from '@/lib/api/client'
import { PageHeader } from '@/components/layout/PageHeader'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Skeleton } from '@/components/ui/Skeleton'
import { Bot, CheckCircle2, Link2Off, Copy, ExternalLink } from 'lucide-react-native'
import { ErrorBoundary } from '@/components/feedback/ErrorBoundary'
import { useNotificationStore } from '@/stores/useNotificationStore'
import * as Clipboard from 'expo-clipboard'
import { API_BASE_URL } from '@/lib/utils/apiUrl'

interface BotStatus {
  connected: boolean
  login: string | null
  displayName: string | null
  profileImageUrl: string | null
}

const BOT_OAUTH_URL = `${API_BASE_URL}/api/v1/auth/twitch/bot`

export default function AdminBotScreen() {
  const addToast = useNotificationStore((s) => s.addToast)
  const queryClient = useQueryClient()

  const { data, isLoading } = useQuery<BotStatus>({
    queryKey: ['admin', 'bot-status'],
    queryFn: async () => {
      const res = await apiClient.get<{ data: BotStatus }>('/v1/auth/twitch/bot/status')
      return res.data.data
    },
    refetchInterval: (query) => (query.state.data?.connected ? false : 5000),
  })

  const disconnectMutation = useMutation({
    mutationFn: () => apiClient.delete('/v1/auth/twitch/bot'),
    onSuccess: () => {
      addToast('success', 'Platform bot disconnected')
      queryClient.invalidateQueries({ queryKey: ['admin', 'bot-status'] })
    },
    onError: () => addToast('error', 'Failed to disconnect bot'),
  })

  async function copyUrl() {
    await Clipboard.setStringAsync(BOT_OAUTH_URL)
    addToast('success', 'URL copied — paste it into the browser where you are logged in as NomNomzBot')
  }

  function openInNewTab() {
    window.open(BOT_OAUTH_URL, '_blank', 'noopener,noreferrer')
  }

  return (
    <ErrorBoundary>
      <ScrollView
        style={{ flex: 1, backgroundColor: '#0a0b0f' }}
        contentContainerStyle={{ paddingBottom: 32 }}
      >
        <PageHeader title="Platform Bot" subtitle="NomNomzBot — shared bot for all channels" />

        <View className="px-5 pt-4 gap-4">
          {isLoading ? (
            <Skeleton className="h-32 rounded-xl" />
          ) : (
            <View
              className="rounded-xl p-5 gap-4"
              style={{
                backgroundColor: '#16171f',
                borderWidth: 1,
                borderColor: data?.connected ? 'rgba(34,197,94,0.25)' : '#2a2b3a',
              }}
            >
              {/* Header */}
              <View className="flex-row items-center gap-3">
                <View
                  className="h-12 w-12 rounded-xl items-center justify-center"
                  style={{ backgroundColor: 'rgba(124,58,237,0.15)' }}
                >
                  <Bot size={24} color="#8b5cf6" />
                </View>
                <View className="flex-1 gap-1">
                  <Text className="text-base font-semibold text-white">
                    {data?.displayName ?? data?.login ?? 'NomNomzBot'}
                  </Text>
                  <Text className="text-xs" style={{ color: '#5a5b72' }}>
                    Global platform bot · serves all channels without a custom bot
                  </Text>
                </View>
                <Badge
                  variant={data?.connected ? 'success' : 'danger'}
                  label={data?.connected ? 'Connected' : 'Not connected'}
                />
              </View>

              {/* Status row */}
              <View
                className="flex-row items-center gap-2 pt-3"
                style={{ borderTopWidth: 1, borderTopColor: '#2a2b3a' }}
              >
                {data?.connected ? (
                  <>
                    <CheckCircle2 size={14} color="#22c55e" />
                    <Text className="flex-1 text-sm" style={{ color: '#22c55e' }}>
                      Connected as @{data.login}
                    </Text>
                    <Button
                      size="sm"
                      variant="ghost"
                      label="Disconnect"
                      leftIcon={<Link2Off size={12} color="#f87171" />}
                      loading={disconnectMutation.isPending}
                      onPress={() => disconnectMutation.mutate()}
                    />
                  </>
                ) : (
                  <Text className="flex-1 text-sm" style={{ color: '#5a5b72' }}>
                    No bot account connected — IRC cannot join channels
                  </Text>
                )}
              </View>
            </View>
          )}

          {/* Connect section — always visible when not connected */}
          {!data?.connected && !isLoading && (
            <View
              className="rounded-xl p-4 gap-3"
              style={{ backgroundColor: '#16171f', borderWidth: 1, borderColor: '#2a2b3a' }}
            >
              <Text className="text-sm font-semibold text-white">Connect NomNomzBot</Text>
              <Text className="text-sm" style={{ color: '#8889a0', lineHeight: 20 }}>
                You need to authorize this from a browser where you are <Text style={{ color: '#a78bfa' }}>logged into Twitch as NomNomzBot</Text> — not your main account.
                Open an incognito window, log into Twitch as NomNomzBot, then paste the link below.
              </Text>

              {/* Copyable URL */}
              <View
                className="flex-row items-center gap-2 rounded-lg px-3 py-2.5"
                style={{ backgroundColor: '#1e1f2a', borderWidth: 1, borderColor: '#2a2b3a' }}
              >
                <Text
                  className="flex-1 text-xs font-mono"
                  style={{ color: '#8889a0' }}
                  numberOfLines={1}
                >
                  {BOT_OAUTH_URL}
                </Text>
                <Pressable onPress={copyUrl} className="p-1">
                  <Copy size={13} color="#8889a0" />
                </Pressable>
              </View>

              <View className="flex-row gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  label="Copy link"
                  leftIcon={<Copy size={12} color="#d1d5db" />}
                  onPress={copyUrl}
                />
                <Button
                  size="sm"
                  label="Open in new tab"
                  leftIcon={<ExternalLink size={12} color="#fff" />}
                  onPress={openInNewTab}
                />
              </View>
            </View>
          )}

          {/* How it works */}
          <View
            className="rounded-xl p-4 gap-2"
            style={{ backgroundColor: '#16171f', borderWidth: 1, borderColor: '#2a2b3a' }}
          >
            <Text className="text-xs font-semibold uppercase tracking-wider" style={{ color: '#5a5b72' }}>
              How it works
            </Text>
            <Text className="text-sm" style={{ color: '#8889a0', lineHeight: 20 }}>
              The platform bot (NomNomzBot) is authenticated once here and shared across all channels.
              Copy the link above and open it in a browser or incognito tab where you are already signed into Twitch as NomNomzBot.
            </Text>
            <Text className="text-sm" style={{ color: '#8889a0', lineHeight: 20 }}>
              Channels on the Pro tier can override this with their own white-label bot via the
              Integrations page in their dashboard.
            </Text>
          </View>
        </View>
      </ScrollView>
    </ErrorBoundary>
  )
}
