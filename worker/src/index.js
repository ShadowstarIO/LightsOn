const WINDOW_MS = 20 * 60 * 1000;
const RATE_MS = 15 * 60 * 1000;
const HISTORY_MS = 90 * 24 * 60 * 60 * 1000;
const VENUES_URL = "https://api.ffxivvenues.com/venue";
const UA = "LightsOn/0.0.1 (+https://github.com/XozaShadow/LightsOn)";

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type",
  "Access-Control-Max-Age": "86400",
};

export default {
  async fetch(request, env) {
    if (request.method === "OPTIONS")
      return new Response(null, { status: 204, headers: CORS });

    try {
      const url = new URL(request.url);
      if (request.method === "GET" && url.pathname === "/")
        return json({ name: "LightsOn", windowMinutes: 20, occupancy: "/v1/occupancy" });
      if (request.method === "GET" && url.pathname === "/v1/health")
        return json(await health(env));
      if (request.method === "GET" && url.pathname === "/v1/occupancy")
        return json(await getOccupancy(env, url.searchParams));
      if (request.method === "POST" && url.pathname === "/v1/reports")
        return await postReport(env, request);
      return json({ error: "not found" }, 404);
    } catch (err) {
      console.error(err);
      return json({ error: "server error" }, 500);
    }
  },

  async scheduled(_event, env) {
    await refreshVenues(env);
    await recomputeAll(env);
    await env.DB.prepare("DELETE FROM reports WHERE at < ?")
      .bind(new Date(Date.now() - HISTORY_MS).toISOString())
      .run();
  },
};

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json; charset=utf-8", ...CORS },
  });
}

async function health(env) {
  const venues = await env.DB.prepare("SELECT COUNT(*) AS n FROM venues").first();
  const occ = await env.DB.prepare("SELECT COUNT(*) AS n FROM occupancy WHERE expires_at > ?")
    .bind(new Date().toISOString())
    .first();
  return { ok: true, venues: venues?.n ?? 0, occupancy: occ?.n ?? 0 };
}

async function getOccupancy(env, params) {
  const now = new Date().toISOString();
  const dc = (params.get("dc") || "").trim();
  const world = (params.get("world") || "").trim();
  let sql = `
    SELECT o.venue_id AS venueId, o.state, o.happening_reports AS happeningReports,
           o.wrapped_up_reports AS wrappedUpReports, o.updated_at AS updatedAt, o.expires_at AS expiresAt
    FROM occupancy o
    LEFT JOIN venues v ON v.id = o.venue_id
    WHERE o.expires_at > ?`;
  const binds = [now];
  if (world) {
    sql += " AND v.world = ? COLLATE NOCASE";
    binds.push(world);
  }
  if (dc) {
    sql += " AND v.data_center = ? COLLATE NOCASE";
    binds.push(dc);
  }
  sql += " ORDER BY o.updated_at DESC";
  const { results } = await env.DB.prepare(sql).bind(...binds).all();
  return results ?? [];
}

async function postReport(env, request) {
  const ctype = request.headers.get("content-type") || "";
  if (!ctype.includes("application/json"))
    return json({ error: "json required" }, 415);

  let body;
  try {
    body = await request.json();
  } catch {
    return json({ error: "invalid json" }, 400);
  }

  const err = validateReport(body);
  if (err)
    return json({ error: err }, 400);

  await ensureVenues(env);
  const venue = await env.DB.prepare("SELECT * FROM venues WHERE id = ?").bind(body.venueId).first();
  if (!venue)
    return json({ error: "unknown venue" }, 400);
  if (!plotMatches(venue, body.proof))
    return json({ error: "proof does not match listed plot" }, 400);

  const now = new Date();
  const at = parseAt(body.at, now);
  const since = new Date(now.getTime() - RATE_MS).toISOString();
  const recent = await env.DB.prepare(
    "SELECT id FROM reports WHERE reporter_id = ? AND venue_id = ? AND at >= ? LIMIT 1",
  ).bind(body.reporterId, body.venueId, since).first();
  if (recent)
    return json({ error: "already reported this venue recently" }, 429);

  const proof = body.proof;
  await env.DB.prepare(
    `INSERT INTO reports (venue_id, kind, reporter_id, at, world, district, ward, plot, subdivision, inside, threshold_met, source)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'plugin')`,
  ).bind(
    body.venueId,
    body.kind,
    body.reporterId,
    at.toISOString(),
    String(proof.world).trim(),
    String(proof.district || "").trim(),
    Number(proof.ward),
    Number(proof.plot),
    proof.subdivision ? 1 : 0,
    proof.inside ? 1 : 0,
    proof.thresholdMet ? 1 : 0,
  ).run();

  await coalesceVenue(env, body.venueId);
  const occupancy = await env.DB.prepare(
    `SELECT venue_id AS venueId, state, happening_reports AS happeningReports,
            wrapped_up_reports AS wrappedUpReports, updated_at AS updatedAt, expires_at AS expiresAt
     FROM occupancy WHERE venue_id = ?`,
  ).bind(body.venueId).first();

  return json({ ok: true, occupancy: occupancy ?? { venueId: body.venueId, state: "unknown" } });
}

function validateReport(body) {
  if (body == null || typeof body !== "object")
    return "invalid body";
  for (const key of ["count", "playerCount", "players", "names", "characters"]) {
    if (key in body || (body.proof && key in body.proof))
      return "counts and names are not accepted";
  }
  if (!/^[A-Za-z0-9_-]{4,32}$/.test(body.venueId || ""))
    return "bad venueId";
  if (body.kind !== "happening" && body.kind !== "wrapped_up")
    return "kind must be happening or wrapped_up";
  if (!/^[a-f0-9]{16,64}$/i.test(body.reporterId || ""))
    return "bad reporterId";
  const proof = body.proof;
  if (proof == null || typeof proof !== "object")
    return "proof required";
  if (typeof proof.thresholdMet !== "boolean")
    return "proof.thresholdMet required";
  if (body.kind === "happening" && !proof.thresholdMet)
    return "happening requires thresholdMet";
  if (body.kind === "wrapped_up" && proof.thresholdMet)
    return "wrapped_up rejected when thresholdMet";
  if (typeof proof.world !== "string" || proof.world.trim().length < 2)
    return "proof.world required";
  const ward = Number(proof.ward);
  const plot = Number(proof.plot);
  if (!Number.isInteger(ward) || ward < 1 || ward > 30)
    return "proof.ward required";
  if (!Number.isInteger(plot) || plot < 1 || plot > 60)
    return "proof.plot required";
  return null;
}

function plotMatches(venue, proof) {
  if (!eq(venue.world, proof.world))
    return false;
  if (venue.district && proof.district && !eq(venue.district, proof.district))
    return false;
  if (Number(venue.ward) !== Number(proof.ward))
    return false;
  if (Number(venue.plot) !== Number(proof.plot))
    return false;
  if (Number(venue.subdivision) === 1 && !proof.subdivision)
    return false;
  return true;
}

function eq(a, b) {
  return String(a || "").trim().toLowerCase() === String(b || "").trim().toLowerCase();
}

function parseAt(value, fallback) {
  if (typeof value !== "string" || value.length === 0)
    return fallback;
  const d = new Date(value);
  if (Number.isNaN(d.getTime()))
    return fallback;
  const drift = Math.abs(d.getTime() - fallback.getTime());
  if (drift > 10 * 60 * 1000)
    return fallback;
  return d;
}

async function coalesceVenue(env, venueId) {
  const since = new Date(Date.now() - WINDOW_MS).toISOString();
  const row = await env.DB.prepare(
    `SELECT
       COUNT(DISTINCT CASE WHEN kind = 'happening' THEN reporter_id END) AS happening_reports,
       COUNT(DISTINCT CASE WHEN kind = 'wrapped_up' THEN reporter_id END) AS wrapped_up_reports,
       MAX(at) AS updated_at
     FROM reports
     WHERE venue_id = ? AND at >= ?`,
  ).bind(venueId, since).first();

  const happening = Number(row?.happening_reports || 0);
  const wrapped = Number(row?.wrapped_up_reports || 0);
  if (happening === 0 && wrapped === 0) {
    await env.DB.prepare("DELETE FROM occupancy WHERE venue_id = ?").bind(venueId).run();
    return;
  }

  const state = happening > 0 ? "happening" : "wrapped_up";
  const updated = row.updated_at || new Date().toISOString();
  const expires = new Date(new Date(updated).getTime() + WINDOW_MS).toISOString();
  await env.DB.prepare(
    `INSERT INTO occupancy (venue_id, state, happening_reports, wrapped_up_reports, updated_at, expires_at)
     VALUES (?, ?, ?, ?, ?, ?)
     ON CONFLICT(venue_id) DO UPDATE SET
       state = excluded.state,
       happening_reports = excluded.happening_reports,
       wrapped_up_reports = excluded.wrapped_up_reports,
       updated_at = excluded.updated_at,
       expires_at = excluded.expires_at`,
  ).bind(venueId, state, happening, wrapped, updated, expires).run();
}

async function recomputeAll(env) {
  const since = new Date(Date.now() - WINDOW_MS).toISOString();
  await env.DB.prepare("DELETE FROM occupancy").run();
  const { results } = await env.DB.prepare(
    `SELECT venue_id,
            COUNT(DISTINCT CASE WHEN kind = 'happening' THEN reporter_id END) AS happening_reports,
            COUNT(DISTINCT CASE WHEN kind = 'wrapped_up' THEN reporter_id END) AS wrapped_up_reports,
            MAX(at) AS updated_at
     FROM reports
     WHERE at >= ?
     GROUP BY venue_id`,
  ).bind(since).all();

  const stmts = [];
  for (const row of results ?? []) {
    const happening = Number(row.happening_reports || 0);
    const wrapped = Number(row.wrapped_up_reports || 0);
    if (happening === 0 && wrapped === 0)
      continue;
    const state = happening > 0 ? "happening" : "wrapped_up";
    const updated = row.updated_at || new Date().toISOString();
    const expires = new Date(new Date(updated).getTime() + WINDOW_MS).toISOString();
    stmts.push(
      env.DB.prepare(
        `INSERT INTO occupancy (venue_id, state, happening_reports, wrapped_up_reports, updated_at, expires_at)
         VALUES (?, ?, ?, ?, ?, ?)`,
      ).bind(row.venue_id, state, happening, wrapped, updated, expires),
    );
  }
  for (let i = 0; i < stmts.length; i += 40)
    await env.DB.batch(stmts.slice(i, i + 40));
}

async function ensureVenues(env) {
  const row = await env.DB.prepare("SELECT COUNT(*) AS n FROM venues").first();
  if ((row?.n ?? 0) === 0)
    await refreshVenues(env);
}

async function refreshVenues(env) {
  const res = await fetch(VENUES_URL, { headers: { "User-Agent": UA } });
  if (!res.ok)
    throw new Error(`venues ${res.status}`);
  const list = await res.json();
  if (!Array.isArray(list) || list.length === 0)
    return;

  const now = new Date().toISOString();
  const stmts = [env.DB.prepare("DELETE FROM venues")];
  for (const v of list) {
    if (!v?.id || !v?.name || !v.location)
      continue;
    const loc = v.location;
    stmts.push(
      env.DB.prepare(
        `INSERT INTO venues (id, name, data_center, world, district, ward, plot, subdivision, updated_at)
         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
      ).bind(
        String(v.id),
        String(v.name),
        String(loc.dataCenter || ""),
        String(loc.world || ""),
        String(loc.district || ""),
        Number(loc.ward) || 0,
        Number(loc.plot) || 0,
        loc.subdivision ? 1 : 0,
        now,
      ),
    );
  }

  for (let i = 0; i < stmts.length; i += 40)
    await env.DB.batch(stmts.slice(i, i + 40));
}
