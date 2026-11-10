# YouTube scope verification: the film and the text for Google

Owner request 2026-11-10: "would you be able to direct a movie that will satisfy the verificaiton process?
use the cursor visibly and so on?" and "i rather just use my real account".

Google verifies a sensitive scope only when a demo video shows the app using it
(support.google.com/cloud/answer/13464321). This page is the shooting script and the text to paste.

Status of the shots: written from the code on 2026-11-10. Nobody ran them yet. Run each shot once without
recording before the real take.

## 1. The scopes to submit

The app asks for five scopes. Remove the other two from the Google project before you submit.

| Scope | The app asks for it | Feature that shows it |
| --- | --- | --- |
| `youtube` | at Connect | Clips tab: upload a clip to YouTube. Music: playlists and ratings |
| `youtube.force-ssl` | at Connect | Chat: send a message, delete a message, ban a viewer |
| `youtube.readonly` | at "See your YouTube analytics" | Analytics, YouTube tab (the report call needs it) |
| `yt-analytics-monetary.readonly` | at "See your YouTube analytics" | Analytics, YouTube tab: views, watch time, revenue |
| `youtube.channel-memberships.creator` | at "See your channel members" | People, YouTube members tab. Supporters, YouTube members tile |
| `youtubepartner` | never | none. Remove it |
| `youtube.upload` | never | none. `youtube` already allows the upload. Remove it |

One thing only the owner can do: `members.list` works only for apps Google approved for it. Ask Google
for that access (the form is linked on the `members.list` page of the YouTube Data API docs). Without it the
members tab shows "The bot may not read your YouTube members yet", and shot 5 cannot show a list.

## 2. Before you record

- Language: set the dashboard and the Google account to English. Google wants the consent screen in English.
- Account: the real account is fine. Use a YouTube channel that has channel memberships and is in the
  YouTube Partner Program, or shots 4 and 5 show only the "not available" texts.
- The menu item is Settings, then Integrations. The page it opens is titled "Connected services". The shots
  below say "Connected services" for that page.
- Disconnect YouTube first (the YouTube row on that page), so the film shows the full grant.
- Overlays: they do not have to be off. Keep the browser window clear of anything that hides the address bar,
  because Google checks the address (dev.nomnomz.bot) and the app name on the consent screen.
- Hide nothing on the consent screen. The whole list of permissions must be readable. Scroll it slowly.
- Record in one take if you can, at 1080p, with the cursor visible. Sound is optional. No cuts inside a shot.
- A VOD is fine as the source. Cut it to the shots below and upload it to YouTube as unlisted.

## 3. The shots

Move the cursor slowly. Stop for two seconds on every button before you click it.

### Shot 1: the app and the sign-in (about 20 seconds)

1. Open `https://dev.nomnomz.bot`. Hold on the home page with the address bar in view.
2. Sign in. Hold on the dashboard home.

### Shot 2: connect YouTube, scopes `youtube` and `youtube.force-ssl` (about 60 seconds)

1. Open Settings, then Connected services.
2. On the YouTube row, click Connect.
3. Google's consent screen opens. Hold. Show the app name and the address `accounts.google.com`.
4. Scroll the permission list slowly, top to bottom.
5. Click Continue. You are back on Connected services. Hold on the YouTube row: it says connected.

### Shot 3: use `youtube.force-ssl` and `youtube` (about 90 seconds)

Your YouTube channel must be live for the chat part.

1. Open Chat. A YouTube chat message is in the feed.
2. Type a message in the composer and send it. Show it in the feed, then show it in the YouTube live chat page.
3. In the dashboard, open the menu of a YouTube message and click "Delete message". Show it gone on YouTube.
4. Open Stream, then the Clips tab. Hold on the list and on "YouTube uploads left today".
5. On one clip, click Upload to YouTube. The dialog opens with the clip's title and "Private: only you".
6. Click Upload. Hold on "The clip is on your YouTube channel." Click Open the video. Hold on the video page.

### Shot 4: analytics, scopes `youtube.readonly` and `yt-analytics-monetary.readonly` (about 60 seconds)

1. Open Settings, then Connected services. On the YouTube row, open Details.
2. Click "See your YouTube analytics". Google's consent screen opens. Hold, scroll the list, click Continue.
3. Open Analytics, then the YouTube tab.
4. Hold on the table: day, views, watch time, subscribers gained and lost, estimated revenue.
5. Change the range to 90 days. Hold.

### Shot 5: members, scope `youtube.channel-memberships.creator` (about 60 seconds)

1. Open Settings, then Connected services. On the YouTube row, open Details.
2. Click "See your channel members". Google's consent screen opens. Hold, scroll the list, click Continue.
3. Open People, then the YouTube members tab. Hold on the list: name, level, member since, months.
4. Open Supporters. Hold on the "YouTube members" tile.

### Shot 6: the user stays in control (about 20 seconds)

1. Open Settings, then Connected services.
2. On the YouTube row, click Disconnect and confirm. Hold on the row: it says not connected.

## 4. The text for Google

Paste this in the scope justification field. It is 947 characters (counted with `wc -c`).

> NomNomzBot is a chat bot and dashboard for live streamers. A streamer connects their own YouTube channel.
> youtube.force-ssl: the bot reads the streamer's live chat, answers commands, and deletes messages or bans
> users when the streamer, a moderator or the streamer's own rules ask for it. youtube: the streamer uploads
> their own stream clips to their own channel from the dashboard, and manages their playlists and ratings
> for song requests. youtube.readonly and yt-analytics-monetary.readonly: the Analytics page shows the
> streamer their own channel's daily views, watch time, subscriber changes and estimated revenue.
> youtube.channel-memberships.creator: the dashboard lists the streamer's own channel members with level
> and join date, so member rewards and member-only commands work. Each permission is asked for only when the
> streamer turns that feature on. Data is shown only to the streamer and their moderators and is never sold
> or shared.

## 5. After the film

- Put the unlisted video link in the "Demo video" field of the Google verification form.
- The privacy policy at `https://dev.nomnomz.bot/privacy-policy` already names the Google data and its use.
- If Google asks for one more scope shot, add it as shot 7 here first, then record only that shot.
