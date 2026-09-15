const WINDOW_MS = 20 * 60 * 1000;
const RATE_MS = 45 * 1000;
const HISTORY_MS = 14 * 24 * 60 * 60 * 1000;
const NOTE_TTL_MS = 14 * 24 * 60 * 60 * 1000;
const NOTE_RATE_MS = 24 * 60 * 60 * 1000;
const MAX_BODY = 8 * 1024;
const VENUES_URL = "https://api.ffxivvenues.com/venue";
const UA = "LightsOn/0.0.3.8 (+https://github.com/XozaShadow/LightsOn)";
const TIER_RANK = { extremely_busy: 3, some_activity: 2, some_wandering: 1 };
const venueCache = new Map();
let migrateTried = false;

const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type, X-LightsOn-Key",
  "Access-Control-Max-Age": "86400",
};

export default {
  async fetch(request, env, ctx) {
    if (request.method === "OPTIONS")
      return new Response(null, { status: 204, headers: CORS });

    try {
      const url = new URL(request.url);
      if (request.method === "GET" && url.pathname === "/")
        return json({ name: "LightsOn", windowMinutes: 20, occupancy: "/v1/occupancy" });
      if (request.method === "GET" && url.pathname === "/v1/health")
        return json(await health(env));
      if (request.method === "GET" && url.pathname === "/v1/occupancy")
        return cachedGet(request, ctx, 60, () => getOccupancy(env, url.searchParams));
      if (request.method === "GET" && url.pathname === "/v1/outdoors")
        return cachedGet(request, ctx, 60, () => getOutdoors(env));
      if (request.method === "GET" && url.pathname === "/v1/notes")
        return json(await getNotes(env, url.searchParams.get("venueId") || ""));
      if (request.method === "GET" && url.pathname === "/v1/reports")
        return cachedGet(request, ctx, 60, () => getReportLog(env, url.searchParams.get("venueId") || ""));
      if (request.method === "POST")
      {
        if (!ingestOk(request, env))
          return json({ error: "unauthorized" }, 401);
        await migrate(env);
      }
      if (request.method === "POST" && url.pathname === "/v1/reports")
        return await limited(request, () => postReport(env, request));
      if (request.method === "POST" && url.pathname === "/v1/notes")
        return await limited(request, () => postNote(env, request));
      if (request.method === "POST" && url.pathname === "/v1/outdoors")
        return await limited(request, () => postOutdoor(env, request));
      return json({ error: "not found" }, 404);
    } catch (err) {
      console.error(err);
      return json({ error: String(err && err.message ? err.message : err) }, 500);
    }
  },

  async scheduled(_event, env) {
    await migrate(env);
    await pruneClosed(env);
    const cutoff = new Date(Date.now() - HISTORY_MS).toISOString();
    await env.DB.prepare("DELETE FROM reports WHERE at < ?").bind(cutoff).run();
    await env.DB.prepare("DELETE FROM notes WHERE expires_at < ?").bind(new Date().toISOString()).run();
    await env.DB.prepare("DELETE FROM outdoors WHERE at < ?").bind(cutoff).run();
    await env.DB.prepare("DELETE FROM outdoor_votes WHERE at < ?").bind(cutoff).run();
  },
};

const postHits = [];

async function limited(request, fn) {
  const now = Date.now();
  while (postHits.length && now - postHits[0] > 60_000)
    postHits.shift();
  if (postHits.length > 30)
    return json({ error: "Too many updates right now. Try again in a minute." }, 429);
  const len = Number(request.headers.get("content-length") || 0);
  if (len > MAX_BODY)
    return json({ error: "payload too large" }, 413);
  postHits.push(now);
  return fn();
}

async function cachedGet(request, ctx, ttlSec, builder) {
  try {
    const cache = caches.default;
    const key = new Request(request.url, { method: "GET" });
    const hit = await cache.match(key);
    if (hit)
      return hit;
    const body = await builder();
    const res = json(body);
    res.headers.set("Cache-Control", `public, max-age=${ttlSec}`);
    if (ctx && typeof ctx.waitUntil === "function")
      ctx.waitUntil(cache.put(key, res.clone()).catch(() => {}));
    return res;
  } catch (err) {
    console.error(err);
    return json(await builder());
  }
}

function json(body, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json; charset=utf-8", ...CORS },
  });
}

function ingestOk(request, env) {
  const expected = String(env.INGEST_KEY || "");
  if (expected.length < 16)
    return false;
  const got = String(request.headers.get("X-LightsOn-Key") || "");
  if (got.length !== expected.length)
    return false;
  let diff = 0;
  for (let i = 0; i < expected.length; i++)
    diff |= expected.charCodeAt(i) ^ got.charCodeAt(i);
  return diff === 0;
}

async function health(env) {
  const since = new Date(Date.now() - WINDOW_MS).toISOString();
  const reports = await env.DB.prepare("SELECT COUNT(*) AS n FROM reports WHERE at >= ?").bind(since).first();
  return { ok: true, reports: reports?.n ?? 0, windowMinutes: 20 };
}

async function getOccupancy(env, params) {
  void params;
  const since = new Date(Date.now() - WINDOW_MS).toISOString();
  const { results } = await env.DB.prepare(
    `SELECT venue_id AS venueId,
            COUNT(DISTINCT CASE WHEN kind = 'happening' THEN reporter_id END) AS happeningReports,
            COUNT(DISTINCT CASE WHEN kind = 'wrapped_up' THEN reporter_id END) AS wrappedUpReports,
            COUNT(DISTINCT CASE WHEN kind = 'happening' AND inside = 1 THEN reporter_id END) AS interiorHappening,
            COUNT(DISTINCT CASE WHEN kind = 'wrapped_up' AND inside = 1 THEN reporter_id END) AS interiorWrapped,
            COUNT(DISTINCT CASE WHEN kind = 'happening' AND inside = 0 THEN reporter_id END) AS exteriorHappening,
            COUNT(DISTINCT CASE WHEN kind = 'wrapped_up' AND inside = 0 THEN reporter_id END) AS exteriorWrapped,
            MAX(door_locked) AS doorLocked,
            MAX(at) AS updatedAt
     FROM reports
     WHERE at >= ?
     GROUP BY venue_id
     ORDER BY MAX(at) DESC
     LIMIT 200`,
  ).bind(since).all();
  return (results ?? []).map((row) => occupancyFromAgg(row));
}

function occupancyFromAgg(row) {
  const happening = Number(row.happeningReports || 0);
  const wrapped = Number(row.wrappedUpReports || 0);
  const intH = Number(row.interiorHappening || 0);
  const intW = Number(row.interiorWrapped || 0);
  const extH = Number(row.exteriorHappening || 0);
  const extW = Number(row.exteriorWrapped || 0);
  const interior = intH + intW > 0;
  const exterior = extH + extW > 0;
  const lean = intH * 2 + extH - intW * 2 - extW;
  let state = "unknown";
  if (happening === 0 && wrapped === 0)
    state = "unknown";
  else if (lean > 0)
    state = "happening";
  else if (lean < 0)
    state = "wrapped_up";
  else
    state = "mixed";
  const updated = row.updatedAt || new Date().toISOString();
  return {
    venueId: row.venueId,
    state,
    happeningReports: happening,
    wrappedUpReports: wrapped,
    interiorHappening: intH,
    interiorWrapped: intW,
    exteriorHappening: extH,
    exteriorWrapped: extW,
    doorLocked: Number(row.doorLocked) === 1,
    bothLayers: interior && exterior,
    updatedAt: updated,
    expiresAt: new Date(new Date(updated).getTime() + WINDOW_MS).toISOString(),
  };
}

async function postReport(env, request) {
  const body = await readJson(request);
  if (!body)
    return json({ error: "invalid json" }, 400);
  const err = validateReport(body);
  if (err)
    return json({ error: err }, 400);

  const venue = await lookupVenue(body.venueId);
  if (!venue)
    return json({ error: "unknown venue" }, 400);
  if (!plotMatches(venue, body.proof))
    return json({ error: "proof does not match listed plot" }, 400);
  if (!venue.open_now)
    return json({ error: "venue is not in posted hours" }, 400);

  const now = new Date();
  const at = parseAt(body.at, now);
  const since = new Date(now.getTime() - RATE_MS).toISOString();
  const proof = body.proof;
  const inside = proof.inside ? 1 : 0;
  const door = proof.doorLocked ? 1 : 0;
  let recent = null;
  try {
    recent = await env.DB.prepare(
      "SELECT id FROM reports WHERE reporter_id = ? AND venue_id = ? AND kind = ? AND inside = ? AND door_locked = ? AND at >= ? LIMIT 1",
    ).bind(body.reporterId, body.venueId, body.kind, inside, door, since).first();
  } catch {
    recent = await env.DB.prepare(
      "SELECT id FROM reports WHERE reporter_id = ? AND venue_id = ? AND kind = ? AND inside = ? AND at >= ? LIMIT 1",
    ).bind(body.reporterId, body.venueId, body.kind, inside, since).first();
  }
  if (recent)
    return json({ error: "already reported this recently" }, 429);

  try {
    await env.DB.prepare(
      `INSERT INTO reports (venue_id, kind, reporter_id, at, world, district, ward, plot, subdivision, inside, threshold_met, door_locked, voices, glance, music, source)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'plugin')`,
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
      inside,
      proof.thresholdMet ? 1 : 0,
      proof.doorLocked ? 1 : 0,
      proof.voices ? 1 : 0,
      proof.glance ? 1 : 0,
      proof.music ? 1 : 0,
    ).run();
  } catch (err) {
    const msg = String(err && err.message ? err.message : err);
    if (!/no such column|no column named/i.test(msg))
      throw err;
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
      inside,
      proof.thresholdMet ? 1 : 0,
    ).run();
  }

  return json({ ok: true, venueId: body.venueId });
}

async function getReportLog(env, venueId) {
  if (!/^[A-Za-z0-9_-]{4,32}$/.test(venueId))
    return [];
  const since = new Date(Date.now() - WINDOW_MS).toISOString();
  try {
    const { results } = await env.DB.prepare(
      `SELECT kind, at, inside, threshold_met AS thresholdMet,
              door_locked AS doorLocked, voices, glance, music
       FROM reports
       WHERE venue_id = ? AND at >= ?
       ORDER BY at DESC LIMIT 24`,
    ).bind(venueId, since).all();
    return mapLog(results);
  } catch {
    const { results } = await env.DB.prepare(
      `SELECT kind, at, inside, threshold_met AS thresholdMet
       FROM reports
       WHERE venue_id = ? AND at >= ?
       ORDER BY at DESC LIMIT 24`,
    ).bind(venueId, since).all();
    return mapLog(results);
  }
}

function mapLog(results) {
  return (results ?? []).map((row) => ({
    kind: row.kind,
    at: row.at,
    inside: Number(row.inside) === 1,
    thresholdMet: Number(row.thresholdMet) === 1,
    doorLocked: Number(row.doorLocked) === 1,
    voices: Number(row.voices) === 1,
    glance: Number(row.glance) === 1,
    music: Number(row.music) === 1,
  }));
}

async function getNotes(env, venueId) {
  if (!/^[A-Za-z0-9_-]{4,32}$/.test(venueId))
    return [];
  const { results } = await env.DB.prepare(
    `SELECT text, at AS at, expires_at AS expiresAt FROM notes
     WHERE venue_id = ? AND expires_at > ?
     ORDER BY at DESC LIMIT 12`,
  ).bind(venueId, new Date().toISOString()).all();
  return results ?? [];
}

async function postNote(env, request) {
  const body = await readJson(request);
  if (!body)
    return json({ error: "invalid json" }, 400);
  const text = sanitizeNote(body.text);
  if (!text)
    return json({ error: "note must be short, no links" }, 400);
  if (!/^[A-Za-z0-9_-]{4,32}$/.test(body.venueId || ""))
    return json({ error: "bad venueId" }, 400);
  if (!/^[a-f0-9]{16,64}$/i.test(body.reporterId || ""))
    return json({ error: "bad reporterId" }, 400);
  if (!body.proof || !plotLooksValid(body.proof))
    return json({ error: "proof required" }, 400);

  const since = new Date(Date.now() - WINDOW_MS).toISOString();
  const lit = await env.DB.prepare(
    "SELECT id FROM reports WHERE venue_id = ? AND kind = 'happening' AND at >= ? LIMIT 1",
  ).bind(body.venueId, since).first();
  if (!lit)
    return json({ error: "log book only while lanterns are lit" }, 400);

  const venue = await lookupVenue(body.venueId);
  if (!venue || !plotMatches(venue, body.proof))
    return json({ error: "proof does not match listed plot" }, 400);

  const now = new Date();
  const dayAgo = new Date(now.getTime() - NOTE_RATE_MS).toISOString();
  const recent = await env.DB.prepare(
    "SELECT id FROM notes WHERE reporter_id = ? AND venue_id = ? AND at >= ? LIMIT 1",
  ).bind(body.reporterId, body.venueId, dayAgo).first();
  if (recent)
    return json({ error: "already left a note here today" }, 429);

  const expires = new Date(now.getTime() + NOTE_TTL_MS).toISOString();
  await env.DB.prepare(
    "INSERT INTO notes (venue_id, reporter_id, text, at, expires_at) VALUES (?, ?, ?, ?, ?)",
  ).bind(body.venueId, body.reporterId, text, now.toISOString(), expires).run();
  await env.DB.prepare(
    `DELETE FROM notes WHERE venue_id = ? AND id NOT IN (
       SELECT id FROM notes WHERE venue_id = ? ORDER BY at DESC LIMIT 12
     )`,
  ).bind(body.venueId, body.venueId).run();
  return json({ ok: true });
}

async function getOutdoors(env) {
  const since = new Date(Date.now() - WINDOW_MS).toISOString();
  const { results } = await env.DB.prepare(
    `SELECT pocket, world, place,
            MAX(CASE tier
              WHEN 'extremely_busy' THEN 3
              WHEN 'some_activity' THEN 2
              ELSE 1 END) AS rank,
            MAX(in_character) AS in_character,
            COUNT(DISTINCT reporter_id) AS reports,
            MAX(at) AS updated_at
     FROM outdoors WHERE at >= ?
     GROUP BY pocket, world, place`,
  ).bind(since).all();

  const votes = await env.DB.prepare(
    `SELECT pocket,
            SUM(CASE WHEN private = 1 THEN 1 ELSE 0 END) AS yes,
            SUM(CASE WHEN private = 0 THEN 1 ELSE 0 END) AS no,
            COUNT(*) AS n
     FROM outdoor_votes WHERE at >= ?
     GROUP BY pocket`,
  ).bind(since).all();
  const voteMap = new Map();
  for (const v of votes.results ?? [])
    voteMap.set(v.pocket, v);

  const out = [];
  for (const row of results ?? []) {
    const v = voteMap.get(row.pocket);
    if (v && v.n > 0) {
      const yes = Number(v.yes) / Number(v.n);
      const no = Number(v.no) / Number(v.n);
      if (yes >= 0.33 && no < 0.25)
        continue;
    }
    const tier = Number(row.rank) >= 3 ? "extremely_busy" : Number(row.rank) >= 2 ? "some_activity" : "some_wandering";
    out.push({
      pocket: row.pocket,
      world: row.world,
      place: row.place,
      tier,
      inCharacter: Number(row.in_character) === 1,
      reports: Number(row.reports || 0),
      updatedAt: row.updated_at,
    });
  }
  out.sort((a, b) => (TIER_RANK[b.tier] || 0) - (TIER_RANK[a.tier] || 0));
  return out.slice(0, 80);
}

async function postOutdoor(env, request) {
  const body = await readJson(request);
  if (!body)
    return json({ error: "invalid json" }, 400);
  if (!/^[A-Za-z0-9_|.:-]{4,80}$/.test(body.pocket || ""))
    return json({ error: "bad pocket" }, 400);
  if (!/^[a-f0-9]{16,64}$/i.test(body.reporterId || ""))
    return json({ error: "bad reporterId" }, 400);
  const tier = String(body.tier || "");
  if (!TIER_RANK[tier])
    return json({ error: "bad tier" }, 400);
  const world = String(body.world || "").trim().slice(0, 32);
  const place = String(body.place || "").trim().slice(0, 64);
  if (world.length < 2 || place.length < 2)
    return json({ error: "world and place required" }, 400);

  const now = new Date();
  const since = new Date(now.getTime() - RATE_MS).toISOString();
  const recent = await env.DB.prepare(
    "SELECT id FROM outdoors WHERE reporter_id = ? AND pocket = ? AND at >= ? LIMIT 1",
  ).bind(body.reporterId, body.pocket, since).first();
  if (recent)
    return json({ error: "already noted this pocket recently" }, 429);

  await env.DB.prepare(
    "INSERT INTO outdoors (pocket, world, place, tier, in_character, reporter_id, at) VALUES (?, ?, ?, ?, ?, ?, ?)",
  ).bind(body.pocket, world, place, tier, body.inCharacter ? 1 : 0, body.reporterId, now.toISOString()).run();

  if (typeof body.privateGathering === "boolean") {
    await env.DB.prepare(
      `INSERT INTO outdoor_votes (pocket, reporter_id, private, at) VALUES (?, ?, ?, ?)
       ON CONFLICT(pocket, reporter_id) DO UPDATE SET private = excluded.private, at = excluded.at`,
    ).bind(body.pocket, body.reporterId, body.privateGathering ? 1 : 0, now.toISOString()).run();
  }
  return json({ ok: true });
}

async function readJson(request) {
  const ctype = request.headers.get("content-type") || "";
  if (!ctype.includes("application/json"))
    return null;
  try {
    return await request.json();
  } catch {
    return null;
  }
}

const LOG_PHRASES = new Set([
  "Kind host",
  "Great music",
  "Warm crowd",
  "Quiet corner",
  "Come again",
  "Short wait",
  "Fine drinks",
  "Good floor",
  "Friendly door",
  "Worth the walk",
]);

function sanitizeNote(raw) {
  const s = String(raw || "").replace(/\s+/g, " ").trim();
  return LOG_PHRASES.has(s) ? s : null;
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
  if (!plotLooksValid(proof))
    return "proof required";
  if (typeof proof.thresholdMet !== "boolean")
    return "proof.thresholdMet required";
  if (body.kind === "happening" && !proof.thresholdMet)
    return "happening requires thresholdMet";
  if (body.kind === "wrapped_up" && proof.thresholdMet)
    return "wrapped_up rejected when thresholdMet";
  return null;
}

function plotLooksValid(proof) {
  if (proof == null || typeof proof !== "object")
    return false;
  if (typeof proof.world !== "string" || proof.world.trim().length < 2)
    return false;
  const ward = Number(proof.ward);
  const plot = Number(proof.plot);
  return Number.isInteger(ward) && ward >= 1 && ward <= 30 && Number.isInteger(plot) && plot >= 1 && plot <= 60;
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
  if (Math.abs(d.getTime() - fallback.getTime()) > 10 * 60 * 1000)
    return fallback;
  return d;
}

async function coalesceVenue(env, venueId) {
  const since = new Date(Date.now() - WINDOW_MS).toISOString();
  const row = await env.DB.prepare(
    `SELECT
       COUNT(DISTINCT CASE WHEN kind = 'happening' THEN reporter_id END) AS happening_reports,
       COUNT(DISTINCT CASE WHEN kind = 'wrapped_up' THEN reporter_id END) AS wrapped_up_reports,
       COUNT(DISTINCT CASE WHEN kind = 'happening' AND inside = 1 THEN reporter_id END) AS interior_happening,
       COUNT(DISTINCT CASE WHEN kind = 'wrapped_up' AND inside = 1 THEN reporter_id END) AS interior_wrapped,
       COUNT(DISTINCT CASE WHEN kind = 'happening' AND inside = 0 THEN reporter_id END) AS exterior_happening,
       COUNT(DISTINCT CASE WHEN kind = 'wrapped_up' AND inside = 0 THEN reporter_id END) AS exterior_wrapped,
       MAX(door_locked) AS door_locked,
       MAX(at) AS updated_at
     FROM reports
     WHERE venue_id = ? AND at >= ?`,
  ).bind(venueId, since).first();

  const happening = Number(row?.happening_reports || 0);
  const wrapped = Number(row?.wrapped_up_reports || 0);
  const intH = Number(row?.interior_happening || 0);
  const intW = Number(row?.interior_wrapped || 0);
  const extH = Number(row?.exterior_happening || 0);
  const extW = Number(row?.exterior_wrapped || 0);
  const door = Number(row?.door_locked || 0) === 1 ? 1 : 0;
  if (happening === 0 && wrapped === 0) {
    await env.DB.prepare("DELETE FROM occupancy WHERE venue_id = ?").bind(venueId).run();
    return;
  }
  const interior = intH + intW > 0;
  const exterior = extH + extW > 0;
  const both = interior && exterior ? 1 : 0;
  let state = "unknown";
  if (intH > 0 && extW > 0)
    state = "mixed";
  else if (extH > 0 && intW > 0)
    state = "mixed";
  else if (happening > 0)
    state = "happening";
  else
    state = "wrapped_up";
  const updated = row.updated_at || new Date().toISOString();
  const expires = new Date(new Date(updated).getTime() + WINDOW_MS).toISOString();
  await env.DB.prepare(
    `INSERT INTO occupancy (venue_id, state, happening_reports, wrapped_up_reports,
        interior_happening, interior_wrapped, exterior_happening, exterior_wrapped,
        door_locked, both_layers, updated_at, expires_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
     ON CONFLICT(venue_id) DO UPDATE SET
       state = excluded.state,
       happening_reports = excluded.happening_reports,
       wrapped_up_reports = excluded.wrapped_up_reports,
       interior_happening = excluded.interior_happening,
       interior_wrapped = excluded.interior_wrapped,
       exterior_happening = excluded.exterior_happening,
       exterior_wrapped = excluded.exterior_wrapped,
       door_locked = excluded.door_locked,
       both_layers = excluded.both_layers,
       updated_at = excluded.updated_at,
       expires_at = excluded.expires_at`,
  ).bind(venueId, state, happening, wrapped, intH, intW, extH, extW, door, both, updated, expires).run();
}

async function recomputeAll(env) {
  const since = new Date(Date.now() - WINDOW_MS).toISOString();
  await env.DB.prepare("DELETE FROM occupancy").run();
  const { results } = await env.DB.prepare(
    "SELECT DISTINCT venue_id FROM reports WHERE at >= ?",
  ).bind(since).all();
  for (const row of results ?? [])
    await coalesceVenue(env, row.venue_id);
}

async function lookupVenue(id) {
  const cached = venueCache.get(id);
  if (cached && Date.now() - cached.at < 30 * 60 * 1000)
    return cached.row;
  const res = await fetch(`${VENUES_URL}/${encodeURIComponent(id)}`, { headers: { "User-Agent": UA } });
  if (!res.ok)
    return null;
  const v = await res.json();
  const loc = v.location || {};
  const row = {
    id: String(v.id || id),
    world: String(loc.world || ""),
    district: String(loc.district || ""),
    ward: Number(loc.ward) || 0,
    plot: Number(loc.plot) || 0,
    subdivision: loc.subdivision ? 1 : 0,
    open_now: venueIsOpen(v) ? 1 : 0,
  };
  venueCache.set(id, { at: Date.now(), row });
  return row;
}

function venueIsOpen(v) {
  const sched = Array.isArray(v.schedule) ? v.schedule : [];
  return sched.some((s) => s?.resolution?.isNow);
}

async function pruneClosed(env) {
  const since = new Date(Date.now() - WINDOW_MS).toISOString();
  const { results } = await env.DB.prepare(
    "SELECT DISTINCT venue_id FROM reports WHERE at >= ?",
  ).bind(since).all();
  for (const row of results ?? []) {
    const venue = await lookupVenue(row.venue_id);
    if (venue && !venue.open_now)
      await env.DB.prepare("DELETE FROM reports WHERE venue_id = ?").bind(row.venue_id).run();
  }
}

async function migrate(env) {
  if (migrateTried)
    return;
  migrateTried = true;
  const cols = [
    ["reports", "door_locked"],
    ["reports", "voices"],
    ["reports", "glance"],
    ["reports", "music"],
    ["occupancy", "interior_happening"],
    ["occupancy", "interior_wrapped"],
    ["occupancy", "exterior_happening"],
    ["occupancy", "exterior_wrapped"],
    ["occupancy", "door_locked"],
    ["occupancy", "both_layers"],
  ];
  for (const [table, col] of cols) {
    try {
      await ensureColumn(env, table, col);
    } catch (err) {
      console.error("migrate", table, col, err);
    }
  }
}

async function ensureColumn(env, table, col) {
  const { results } = await env.DB.prepare(`PRAGMA table_info(${table})`).all();
  if ((results || []).some((row) => row.name === col))
    return;
  await env.DB.prepare(`ALTER TABLE ${table} ADD COLUMN ${col} INTEGER DEFAULT 0`).run();
}

