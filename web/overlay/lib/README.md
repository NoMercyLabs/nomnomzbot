# Vendored client libraries

These are third-party browser libraries vendored so the bot-served overlay pages work without any
external CDN (offline / locked-down networks). They are served as static files by the bot at
`/overlay/lib/`.

| File | Library | Version | License |
|------|---------|---------|---------|
| `signalr.min.js` | [`@microsoft/signalr`](https://www.npmjs.com/package/@microsoft/signalr) | 8.0.7 | MIT (© Microsoft) |

To update: replace the file with the matching `dist/browser/*.min.js` from the npm package and bump
the version above.
