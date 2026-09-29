# First-Time Setup Wizard

The app detects when no streamer account is configured and routes to the setup wizard. The wizard:

1. **Connect your first platform** — any of Twitch / Kick / YouTube / X (device code where the
   platform has it); this creates the channel. Further platforms attach as connections (D2).
2. **Connect bot account** — optional separate account the bot types from; until then the bot types
   as the streamer's own account with a user-defined line prefix (D5)
3. **Configure basics** — bot prefix, default language, timezone
4. **Enable integrations** — Spotify, Discord, etc. (can skip and do later from Settings)

After completion, lands on the dashboard home. The wizard is implemented in the dashboard
(device-code onboarding); returning users get quick login or a remembered-session restore —
never a repeat of the device-code dance.
