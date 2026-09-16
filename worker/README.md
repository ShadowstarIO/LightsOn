# LightsOn occupancy worker

Public GET is cached (~60s), including `/` and `/v1/health`. Anonymous reads 30/minute per IP; LightsOn / StatusShift / Shadowstar user-agents 120/minute. Extra prefixes: worker secret/var `READ_ALLOW`.

Host: `https://lightson.shadowstar.io`. Aliases: `https://lightson.xoza.net`, `workers.dev`.

Secret on the repo: `CLOUDFLARE_API_TOKEN`.
