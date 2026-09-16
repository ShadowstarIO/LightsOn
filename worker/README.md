# LightsOn occupancy worker

GitHub Actions deploys this. Public GET is cached (~60s). Cron every 5 minutes. Directory refresh at most every 30 minutes. History 14 days.

Host: `https://lightson.xoza.net` (custom domain on this worker). `workers.dev` stays on as a fallback.

Secret on the repo: `CLOUDFLARE_API_TOKEN`.
