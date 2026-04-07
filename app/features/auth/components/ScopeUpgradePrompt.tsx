import { View, Text, Pressable, Modal } from 'react-native'
import { ShieldCheck, X } from 'lucide-react-native'
import { useAuthStore } from '@/stores/useAuthStore'
import { makeRedirectUri } from 'expo-auth-session'
import * as WebBrowser from 'expo-web-browser'
import { Platform } from 'react-native'
import { API_BASE_URL } from '@/lib/utils/apiUrl'

export function ScopeUpgradePrompt() {
  const pendingScopeUpgrade = useAuthStore((s) => s.pendingScopeUpgrade)
  const dismissScopeUpgrade = useAuthStore((s) => s.dismissScopeUpgrade)
  const isVisible = Array.isArray(pendingScopeUpgrade) && pendingScopeUpgrade.length > 0

  async function handleGrantAccess() {
    if (!pendingScopeUpgrade) return

    const scopeParam = encodeURIComponent(pendingScopeUpgrade.join(' '))

    if (Platform.OS === 'web') {
      const webCallback = typeof window !== 'undefined' ? `${window.location.origin}/callback` : ''
      window.location.href = `${API_BASE_URL}/api/v1/auth/twitch?scopes=${scopeParam}&redirect_uri=${encodeURIComponent(webCallback)}`
      return
    }

    const redirectUri = makeRedirectUri({ scheme: 'nomercybot', path: 'callback' })
    const authUrl = `${API_BASE_URL}/api/v1/auth/twitch?scopes=${scopeParam}&redirect_uri=${encodeURIComponent(redirectUri)}`
    const result = await WebBrowser.openAuthSessionAsync(authUrl, redirectUri)
    if (result.type === 'success') {
      dismissScopeUpgrade()
    }
    // On cancel/dismiss, keep the modal open so the user can retry
  }

  return (
    <Modal
      visible={isVisible}
      transparent
      animationType="fade"
      onRequestClose={dismissScopeUpgrade}
    >
      <View className="flex-1 items-center justify-center bg-black/60 px-6">
        <View
          className="w-full max-w-sm rounded-2xl p-6 gap-5"
          style={{ backgroundColor: '#16171f', borderWidth: 1, borderColor: '#2a2b3a' }}
        >
          {/* Header */}
          <View className="flex-row items-start justify-between gap-3">
            <View className="flex-1 flex-row items-center gap-3">
              <View
                className="w-10 h-10 rounded-full items-center justify-center"
                style={{ backgroundColor: 'rgba(145,71,255,0.2)' }}
              >
                <ShieldCheck size={20} color="#9147ff" strokeWidth={2} />
              </View>
              <Text className="flex-1 text-lg font-bold text-white">
                Additional Permissions Needed
              </Text>
            </View>
            <Pressable
              onPress={dismissScopeUpgrade}
              className="p-1 rounded-lg"
              hitSlop={8}
            >
              <X size={18} color="#5a5b72" strokeWidth={2} />
            </Pressable>
          </View>

          {/* Body */}
          <View className="gap-3">
            <Text className="text-sm" style={{ color: '#8889a0' }}>
              This feature needs additional permissions to work:
            </Text>
            <View className="gap-1.5">
              {(pendingScopeUpgrade ?? []).map((scope) => (
                <View
                  key={scope}
                  className="flex-row items-center gap-2 rounded-lg px-3 py-2"
                  style={{ backgroundColor: '#1e1f2a' }}
                >
                  <View className="w-1.5 h-1.5 rounded-full" style={{ backgroundColor: '#9147ff' }} />
                  <Text className="text-sm font-mono text-white">{scope}</Text>
                </View>
              ))}
            </View>
          </View>

          {/* Actions */}
          <View className="gap-3">
            <Pressable
              onPress={handleGrantAccess}
              className="w-full flex-row items-center justify-center gap-2 rounded-xl py-3.5"
              style={{ backgroundColor: '#9147ff' }}
            >
              <Text className="text-white font-semibold">Grant Access</Text>
            </Pressable>
            <Pressable
              onPress={dismissScopeUpgrade}
              className="w-full items-center py-3"
            >
              <Text style={{ color: '#5a5b72' }}>Not now</Text>
            </Pressable>
          </View>
        </View>
      </View>
    </Modal>
  )
}
