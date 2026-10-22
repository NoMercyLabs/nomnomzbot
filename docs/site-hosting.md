# Site hosting

The main domain `nomnomz.bot` serves a static copy of the Vue site from GitHub Pages. No bot runs
behind it. It has the home page, a "Coming soon" login page and a not-found page. Every sign-in
button is disabled, and the pages make no API call.

The dev box (`dev.nomnomz.bot`, `vue.nomnomz.bot`) is separate. `ci.yml` deploys it. The Pages
workflow never touches it.

## How it is built

- Workflow: `.github/workflows/pages.yml`. It runs on a push to `master` that changes `dashboard/**`
  or the workflow, and on a manual run.
- Build: `npm run build-site` in `dashboard/` (config `vite.site.config.ts`, mode `site`, output
  `dashboard/dist-site`). Sign-in stays closed through `dashboard/.env.site`.
- The build has no service worker and no web manifest. The pages are static and the host caches them.
- The build writes `index.html`, a copy named `404.html` (GitHub serves it for any unknown path, so a
  deep link still loads the app), `CNAME`, `.nojekyll` and `robots.txt`.

Build it locally with `cd dashboard && npm run build-site`, then open `dist-site/` with any static
server.

## Redeploy

Run the workflow again: GitHub, Actions, Pages, Run workflow. Or push a change under `dashboard/`.

## Roll back

Open an earlier green run of the Pages workflow and choose Re-run all jobs. It redeploys that run's
commit. Nothing else needs to change.

## DNS (zone `nomnomz.bot` in Cloudflare)

| Name | Type | Value | Proxy |
|---|---|---|---|
| `nomnomz.bot` | A | `185.199.108.153` | DNS only |
| `nomnomz.bot` | A | `185.199.109.153` | DNS only |
| `nomnomz.bot` | A | `185.199.110.153` | DNS only |
| `nomnomz.bot` | A | `185.199.111.153` | DNS only |
| `www.nomnomz.bot` | CNAME | `nomnomz.bot` | as set |

The apex stays DNS only so GitHub can reach the host and issue its certificate. Change the records
only with `node scripts/cloudflare-nomnomz-dev.mjs pages-apex --backup-dir <dir>`. It prints a dry
run first and writes the changes only with `--yes`. It saves the old records, touches only the names
`nomnomz.bot` and `www.nomnomz.bot` and only the types A, AAAA and CNAME, and reads the Cloudflare key
itself without printing it.

In the repository settings, Pages uses the source "GitHub Actions" with the custom domain
`nomnomz.bot` and "Enforce HTTPS" on.
