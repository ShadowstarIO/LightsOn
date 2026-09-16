const WINDOW_MS = 4 * 60 * 60 * 1000;
const RATE_MS = 45 * 1000;
const NOTE_TTL_MS = 7 * 24 * 60 * 60 * 1000;
const NOTE_RATE_MS = 60 * 60 * 1000;
const OUTDOOR_MS = 4 * 60 * 60 * 1000;
const MAX_BODY = 8 * 1024;
const VENUES_URL = "https://api.ffxivvenues.com/venue";
const UA = "LightsOn/0.0.4.14 (+https://github.com/ShadowstarIO/LightsOn)";
const TIER_RANK = { extremely_busy: 5, busy: 4, some_activity: 3, light_activity: 2, some_wandering: 1 };
const OUTDOOR_LOCK_MS = {
  extremely_busy: 5 * 60 * 1000,
  busy: 6 * 60 * 1000,
  some_activity: 8 * 60 * 1000,
  light_activity: 12 * 60 * 1000,
  some_wandering: 20 * 60 * 1000,
};
const OUTDOOR_UPGRADE_MS = 5 * 60 * 1000;
const OUTDOOR_NEAR_MS = 5 * 60 * 1000;
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
        return json({ name: "LightsOn", windowMinutes: 240, occupancyHours: 4, occupancy: "/v1/occupancy" });
      if (request.method === "GET" && url.pathname === "/v1/health")
        return json(await health(env));
      if (request.method === "GET" && url.pathname === "/v1/occupancy")
        return cachedGet(request, ctx, 60, () => getOccupancy(env, url.searchParams));
      if (request.method === "GET" && url.pathname === "/v1/outdoors")
        return cachedGet(request, ctx, 60, () => getOutdoors(env));
      if (request.method === "GET" && url.pathname === "/v1/notes")
        return cachedGet(request, ctx, 60, () => getNotes(env, url.searchParams.get("venueId") || ""));
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
    const cutoff = new Date(Date.now() - WINDOW_MS).toISOString();
    await env.DB.prepare("DELETE FROM reports WHERE at < ?").bind(cutoff).run();
    await env.DB.prepare("DELETE FROM notes WHERE expires_at < ?").bind(new Date().toISOString()).run();
    const outdoorCutoff = new Date(Date.now() - OUTDOOR_MS).toISOString();
    await env.DB.prepare("DELETE FROM outdoors WHERE at < ?").bind(outdoorCutoff).run();
    await env.DB.prepare("DELETE FROM outdoor_votes WHERE at < ?").bind(outdoorCutoff).run();
  },
};

const postHits = [];

async function limited(request, fn) {
  const now = Date.now();
  while (postHits.length && now - postHits[0] > 60_000)
    postHits.shift();
  if (postHits.length > 80)
    return json({ error: "Too many updates right now. Try again in a minute." }, 429);
  const len = Number(request.headers.get("content-length") || 0);
  if (len > MAX_BODY)
    return json({ error: "payload too large" }, 413);
  const res = await fn();
  if (res.status < 400)
    postHits.push(now);
  return res;
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
  return { ok: true, reports: reports?.n ?? 0, windowMinutes: 240, occupancyHours: 4 };
}

async function getOccupancy(env, params) {
  void params;
  const since = new Date(Date.now() - WINDOW_MS).toISOString();
  let results;
  try {
    ({ results } = await env.DB.prepare(
      `SELECT venue_id AS venueId, kind, reporter_id AS reporterId, at, inside,
              door_locked AS doorLocked
       FROM reports WHERE at >= ? ORDER BY at DESC LIMIT 4000`,
    ).bind(since).all());
  } catch {
    ({ results } = await env.DB.prepare(
      `SELECT venue_id AS venueId, kind, reporter_id AS reporterId, at, inside
       FROM reports WHERE at >= ? ORDER BY at DESC LIMIT 4000`,
    ).bind(since).all());
  }
  const byVenue = new Map();
  for (const row of results ?? []) {
    if (!byVenue.has(row.venueId))
      byVenue.set(row.venueId, []);
    byVenue.get(row.venueId).push(row);
  }
  const out = [];
  for (const [venueId, rows] of byVenue) {
    const venue = await lookupVenue(venueId);
    if (venue && !venue.open_now)
      continue;
    const snap = occupancyFromReports(venueId, rows);
    if (snap.happeningReports + snap.wrappedUpReports > 0)
      out.push(snap);
  }
  return out;
}

function reportWeight(at) {
  const hours = Math.max(0, (Date.now() - new Date(at).getTime()) / 3600000);
  return 1 / Math.max(1, hours);
}

function occupancyFromReports(venueId, rows) {
  const latest = new Map();
  for (const row of rows) {
    const key = `${row.reporterId}|${Number(row.inside) === 1 ? 1 : 0}`;
    if (!latest.has(key))
      latest.set(key, row);
  }
  let happening = 0;
  let wrapped = 0;
  let intH = 0;
  let intW = 0;
  let extH = 0;
  let extW = 0;
  let lean = 0;
  let door = 0;
  let updated = "";
  for (const row of latest.values()) {
    const inside = Number(row.inside) === 1;
    const up = row.kind === "happening";
    const w = reportWeight(row.at);
    lean += w * (inside ? 2 : 1) * (up ? 1 : -1);
    if (up) {
      happening++;
      if (inside) intH++;
      else extH++;
    } else {
      wrapped++;
      if (inside) intW++;
      else extW++;
    }
    if (Number(row.doorLocked) === 1)
      door = 1;
    if (!updated || row.at > updated)
      updated = row.at;
  }
  const interior = intH + intW > 0;
  const exterior = extH + extW > 0;
  let state = "unknown";
  if (happening === 0 && wrapped === 0)
    state = "unknown";
  else if (lean > 0)
    state = "happening";
  else if (lean < 0)
    state = "wrapped_up";
  else
    state = "mixed";
  return {
    venueId,
    state,
    happeningReports: happening,
    wrappedUpReports: wrapped,
    interiorHappening: intH,
    interiorWrapped: intW,
    exteriorHappening: extH,
    exteriorWrapped: extW,
    doorLocked: door === 1,
    bothLayers: interior && exterior,
    updatedAt: updated || new Date().toISOString(),
    expiresAt: new Date(Date.now() + WINDOW_MS).toISOString(),
    lean: Math.round(lean * 100) / 100,
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
  const windowSince = new Date(now.getTime() - WINDOW_MS).toISOString();
  let existing = 0;
  try {
    const row = await env.DB.prepare(
      "SELECT COUNT(*) AS n FROM reports WHERE venue_id = ? AND at >= ?",
    ).bind(body.venueId, windowSince).first();
    existing = Number(row?.n || 0);
  } catch { existing = 0; }
  const need = existing === 0 ? 0 : existing < 3 ? 20 * 1000 : existing < 6 ? RATE_MS : 3 * 60 * 1000;
  const proof = body.proof;
  const inside = proof.inside ? 1 : 0;
  const door = proof.doorLocked ? 1 : 0;
  if (need > 0) {
    const since = new Date(now.getTime() - need).toISOString();
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
  }

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

  try {
    await env.DB.prepare(
      `DELETE FROM reports WHERE venue_id = ? AND id NOT IN (
         SELECT id FROM reports WHERE venue_id = ? ORDER BY at DESC LIMIT 12)`,
    ).bind(body.venueId, body.venueId).run();
  } catch { /* cap is best-effort */ }

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
       ORDER BY at DESC LIMIT 12`,
    ).bind(venueId, since).all();
    return mapLog(results);
  } catch {
    const { results } = await env.DB.prepare(
      `SELECT kind, at, inside, threshold_met AS thresholdMet
       FROM reports
       WHERE venue_id = ? AND at >= ?
       ORDER BY at DESC LIMIT 12`,
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
    return json({ error: "already left a note here this hour" }, 429);

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
  await migrate(env);
  const since = new Date(Date.now() - OUTDOOR_MS).toISOString();
  const { results } = await env.DB.prepare(
    `SELECT pocket, world, place, zone, tier, in_character, patrons, zone_count,
            voices, glance, emotes, score, activity, at AS updated_at
     FROM outdoors WHERE at >= ?
     ORDER BY at DESC LIMIT 200`,
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

  const hidden = new Set();
  for (const [pocket, v] of voteMap) {
    if (!v || v.n <= 0)
      continue;
    const yes = Number(v.yes) / Number(v.n);
    const no = Number(v.no) / Number(v.n);
    if (yes >= 0.33 && no < 0.25)
      hidden.add(pocket);
  }

  const out = [];
  for (const row of results ?? []) {
    if (hidden.has(row.pocket))
      continue;
    out.push({
      pocket: row.pocket,
      world: row.world,
      place: row.place,
      zone: row.zone || "",
      tier: row.tier,
      inCharacter: Number(row.in_character) === 1,
      patrons: Math.min(99, Number(row.patrons || 0)),
      zoneCount: Math.min(99, Number(row.zone_count || 0)),
      voices: Number(row.voices) === 1,
      glance: Number(row.glance) === 1,
      emotes: Number(row.emotes) === 1,
      score: Number(row.score || 0),
      activity: row.activity || "",
      reports: 1,
      updatedAt: row.updated_at,
    });
  }
  return out;
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
  const zone = String(body.zone || "").trim().slice(0, 64);
  if (world.length < 2 || place.length < 2)
    return json({ error: "world and place required" }, 400);
  const activity = String(body.activity || "").trim();
  if (activity && !OUTDOOR_SCENES.has(activity))
    return json({ error: "pick a scene from the list" }, 400);

  const now = new Date();
  const nearSince = new Date(now.getTime() - OUTDOOR_NEAR_MS).toISOString();
  const recent = await env.DB.prepare(
    "SELECT pocket, tier, at FROM outdoors WHERE reporter_id = ? AND at >= ? ORDER BY at DESC LIMIT 24",
  ).bind(body.reporterId, nearSince).all();
  if ((recent.results ?? []).some((row) => nearbyPocket(row.pocket, body.pocket)))
    return json({ error: "already reported near here" }, 429);

  const last = await env.DB.prepare(
    "SELECT pocket, tier, at FROM outdoors WHERE reporter_id = ? ORDER BY at DESC LIMIT 8",
  ).bind(body.reporterId).all();
  for (const row of last.results ?? []) {
    if (!nearbyPocket(row.pocket, body.pocket))
      continue;
    const age = now.getTime() - new Date(row.at).getTime();
    const lastRank = TIER_RANK[row.tier] || 0;
    const nextRank = TIER_RANK[tier] || 0;
    const need = nextRank > lastRank ? OUTDOOR_UPGRADE_MS : (OUTDOOR_LOCK_MS[row.tier] || OUTDOOR_MS);
    if (age < Math.max(RATE_MS, need))
      return json({ error: "already reported this pocket recently" }, 429);
    break;
  }

  const patrons = Math.max(0, Math.min(99, Number(body.patrons) || 0));
  const zoneCount = Math.max(0, Math.min(99, Number(body.zoneCount) || 0));
  const score = Math.max(0, Math.min(20, Number(body.score) || 0));
  await env.DB.prepare(
    `INSERT INTO outdoors (pocket, world, place, zone, tier, in_character, patrons, zone_count,
       voices, glance, emotes, score, activity, reporter_id, at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
  ).bind(
    body.pocket, world, place, zone, tier, body.inCharacter ? 1 : 0,
    patrons, zoneCount, body.voices ? 1 : 0, body.glance ? 1 : 0, body.emotes ? 1 : 0,
    score, activity, body.reporterId, now.toISOString(),
  ).run();

  if (typeof body.privateGathering === "boolean") {
    await env.DB.prepare(
      `INSERT INTO outdoor_votes (pocket, reporter_id, private, at) VALUES (?, ?, ?, ?)
       ON CONFLICT(pocket, reporter_id) DO UPDATE SET private = excluded.private, at = excluded.at`,
    ).bind(body.pocket, body.reporterId, body.privateGathering ? 1 : 0, now.toISOString()).run();
  }
  return json({ ok: true });
}

function nearbyPocket(a, b) {
  const pa = String(a || "").split("|");
  const pb = String(b || "").split("|");
  if (pa.length !== 4 || pb.length !== 4)
    return a === b;
  if (pa[0] !== pb[0] || pa[1] !== pb[1])
    return false;
  const dx = Math.abs(Number(pa[2]) - Number(pb[2]));
  const dz = Math.abs(Number(pa[3]) - Number(pb[3]));
  return Math.max(dx, dz) <= 2;
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

const LOG_ADJ = ["Bright", "Calm", "Cheerful", "Cozy", "Easy", "Fair", "Fine", "Fresh", "Friendly", "Gentle", "Good", "Great", "Happy", "Inviting", "Kind", "Light", "Lively", "Lovely", "Mellow", "Nice", "Open", "Peaceful", "Pleasant", "Polite", "Quiet", "Relaxed", "Smooth", "Soft", "Steady", "Sweet", "Warm", "Welcoming"];
const LOG_NOUN = ["Air", "Bar", "Chat", "Company", "Corner", "Crowd", "Door", "Drinks", "Energy", "Entry", "Floor", "Food", "Hall", "Host", "Lights", "Mix", "Mood", "Music", "Night", "Room", "Scene", "Seats", "Set", "Space", "Staff", "Stage", "Vibe", "Wait", "Walk", "Welcome", "Yard"];
const LOG_PHRASES = new Set(LOG_ADJ.flatMap((a) => LOG_NOUN.map((n) => `${a} ${n}`)));
const OUTDOOR_SCENES = new Set(["Camp", "Dance", "Event", "Fight", "Hunt", "Market", "Parade", "Party", "Performance", "RP", "Social"]);

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
  if (body.kind === "wrapped_up" && proof.thresholdMet && !proof.doorLocked && !proof.unhosted)
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
  const apt = Number(proof.apartment) || 0;
  if (!Number.isInteger(ward) || ward < 1 || ward > 30)
    return false;
  if (Number.isInteger(apt) && apt >= 1 && apt <= 99)
    return true;
  return Number.isInteger(plot) && plot >= 1 && plot <= 60;
}

function canonDistrict(s) {
  const t = String(s || "").toLowerCase();
  if (t.includes("lavender") || t.includes("lily")) return "lavender beds";
  if (t.includes("goblet") || t.includes("sultana")) return "goblet";
  if (t.includes("shirogane") || t.includes("kobai")) return "shirogane";
  if (t.includes("empyreum") || t.includes("ingleside")) return "empyreum";
  if (t.includes("mist") || t.includes("topmast")) return "mist";
  return t.trim();
}

function canonPlot(plot, sub) {
  const p = Number(plot);
  if (Number.isInteger(p) && p >= 1 && p <= 30 && sub) return p + 30;
  return p;
}

function plotMatches(venue, proof) {
  if (!eq(venue.world, proof.world))
    return false;
  const vd = canonDistrict(venue.district);
  const pd = canonDistrict(proof.district);
  if (vd && pd && vd !== pd)
    return false;
  if (Number(venue.ward) !== Number(proof.ward))
    return false;
  const venueApt = Number(venue.apartment) || 0;
  if (venueApt > 0 && Number(venue.plot) === 0) {
    const proofApt = Number(proof.apartment) || 0;
    if (proofApt !== venueApt)
      return false;
    return !!venue.subdivision === !!proof.subdivision;
  }
  if (canonPlot(venue.plot, venue.subdivision) !== canonPlot(proof.plot, proof.subdivision))
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
  let v = cached && Date.now() - cached.at < 30 * 60 * 1000 ? cached.raw : null;
  if (!v) {
    const res = await fetch(`${VENUES_URL}/${encodeURIComponent(id)}`, { headers: { "User-Agent": UA } });
    if (!res.ok)
      return null;
    v = await res.json();
    venueCache.set(id, { at: Date.now(), raw: v });
  }
  const loc = v.location || {};
  return {
    id: String(v.id || id),
    world: String(loc.world || ""),
    district: String(loc.district || ""),
    ward: Number(loc.ward) || 0,
    plot: Number(loc.plot) || 0,
    apartment: Number(loc.apartment || loc.room) || 0,
    subdivision: loc.subdivision ? 1 : 0,
    open_now: venueIsOpen(v) ? 1 : 0,
  };
}

function venueIsOpen(v) {
  if (spanIsNow(v?.resolution) || v?.resolution?.isNow)
    return true;
  const overrides = Array.isArray(v.scheduleOverrides) ? v.scheduleOverrides : [];
  if (overrides.some((o) => o && o.open && (spanIsNow(o) || o.isNow)))
    return true;
  const sched = Array.isArray(v.schedule) ? v.schedule : [];
  return sched.some((s) => spanIsNow(s?.resolution) || s?.resolution?.isNow);
}

function spanIsNow(span) {
  if (!span || !span.start || !span.end)
    return false;
  const a = Date.parse(span.start);
  const b = Date.parse(span.end);
  if (!Number.isFinite(a) || !Number.isFinite(b))
    return false;
  const n = Date.now();
  return n >= a && n < b;
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
    ["outdoors", "patrons"],
    ["outdoors", "zone_count"],
    ["outdoors", "voices"],
    ["outdoors", "glance"],
    ["outdoors", "emotes"],
    ["outdoors", "score"],
  ];
  for (const [table, col] of cols) {
    try {
      await ensureColumn(env, table, col);
    } catch (err) {
      console.error("migrate", table, col, err);
    }
  }
  for (const col of ["zone", "activity"]) {
    try {
      await ensureTextColumn(env, "outdoors", col);
    } catch (err) {
      console.error("migrate outdoors", col, err);
    }
  }
}

async function ensureColumn(env, table, col) {
  const { results } = await env.DB.prepare(`PRAGMA table_info(${table})`).all();
  if ((results || []).some((row) => row.name === col))
    return;
  await env.DB.prepare(`ALTER TABLE ${table} ADD COLUMN ${col} INTEGER DEFAULT 0`).run();
}

async function ensureTextColumn(env, table, col) {
  const { results } = await env.DB.prepare(`PRAGMA table_info(${table})`).all();
  if ((results || []).some((row) => row.name === col))
    return;
  await env.DB.prepare(`ALTER TABLE ${table} ADD COLUMN ${col} TEXT NOT NULL DEFAULT ''`).run();
}

