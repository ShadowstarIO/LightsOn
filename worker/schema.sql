CREATE TABLE IF NOT EXISTS reports (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  venue_id TEXT NOT NULL,
  kind TEXT NOT NULL CHECK (kind IN ('happening', 'wrapped_up')),
  reporter_id TEXT NOT NULL,
  at TEXT NOT NULL,
  world TEXT,
  district TEXT,
  ward INTEGER,
  plot INTEGER,
  subdivision INTEGER NOT NULL DEFAULT 0,
  inside INTEGER NOT NULL DEFAULT 0,
  threshold_met INTEGER NOT NULL DEFAULT 0,
  door_locked INTEGER NOT NULL DEFAULT 0,
  voices INTEGER NOT NULL DEFAULT 0,
  glance INTEGER NOT NULL DEFAULT 0,
  music INTEGER NOT NULL DEFAULT 0,
  source TEXT NOT NULL DEFAULT 'plugin'
);

CREATE INDEX IF NOT EXISTS reports_venue_at ON reports (venue_id, at);
CREATE INDEX IF NOT EXISTS reports_reporter_venue_at ON reports (reporter_id, venue_id, at);

CREATE TABLE IF NOT EXISTS occupancy (
  venue_id TEXT PRIMARY KEY,
  state TEXT NOT NULL,
  happening_reports INTEGER NOT NULL DEFAULT 0,
  wrapped_up_reports INTEGER NOT NULL DEFAULT 0,
  interior_happening INTEGER NOT NULL DEFAULT 0,
  interior_wrapped INTEGER NOT NULL DEFAULT 0,
  exterior_happening INTEGER NOT NULL DEFAULT 0,
  exterior_wrapped INTEGER NOT NULL DEFAULT 0,
  door_locked INTEGER NOT NULL DEFAULT 0,
  both_layers INTEGER NOT NULL DEFAULT 0,
  updated_at TEXT NOT NULL,
  expires_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS venues (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL,
  data_center TEXT,
  world TEXT,
  district TEXT,
  ward INTEGER,
  plot INTEGER,
  subdivision INTEGER NOT NULL DEFAULT 0,
  open_now INTEGER NOT NULL DEFAULT 0,
  updated_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS venues_world ON venues (world);

CREATE TABLE IF NOT EXISTS notes (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  venue_id TEXT NOT NULL,
  reporter_id TEXT NOT NULL,
  text TEXT NOT NULL,
  at TEXT NOT NULL,
  expires_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS notes_venue_at ON notes (venue_id, at);

CREATE TABLE IF NOT EXISTS outdoors (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  pocket TEXT NOT NULL,
  world TEXT NOT NULL,
  place TEXT NOT NULL,
  tier TEXT NOT NULL,
  in_character INTEGER NOT NULL DEFAULT 0,
  reporter_id TEXT NOT NULL,
  at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS outdoors_pocket_at ON outdoors (pocket, at);

CREATE TABLE IF NOT EXISTS outdoor_votes (
  pocket TEXT NOT NULL,
  reporter_id TEXT NOT NULL,
  private INTEGER NOT NULL,
  at TEXT NOT NULL,
  PRIMARY KEY (pocket, reporter_id)
);

CREATE TABLE IF NOT EXISTS meta (
  k TEXT PRIMARY KEY,
  v TEXT NOT NULL
);
