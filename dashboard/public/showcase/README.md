# Showcase pictures

The 16 pictures on the home page: 8 areas, each as a `hero` (with the sidebar) and a `group` (without).
Each is a 1920x1080 webp photograph of the real dashboard page filled with the sample channel `crumb_tv`.

To take them again:

1. Start the dashboard dev server (`npm run dev`).
2. Open `/capture.html` in a window of exactly 1920x1080.
3. Run `await window.showcase.draw('<area>', '<hero|group>')`, then screenshot the window to `<area>-<kind>.webp`.

`window.showcase.views` lists every view. The sample answers live in `src/views/site/home/preview/replies`.
