#!/usr/bin/env python3
"""Collects reach stats for zdbrus/zdbrus-bot and merges them into running history files.

Usage: python3 tools/reach_stats.py <dir>
<dir> may already hold history.json / views_daily.json from earlier runs; they are merged, never lost.
Writes: history.json, views_daily.json, releases.json, referrers.json (JSON arrays of row objects).
Needs `gh` signed in with push access to the repo (traffic data is owner-only).
"""
import json, os, subprocess, sys, datetime

REPO = "zdbrus/zdbrus-bot"


def api(path):
    env = {k: v for k, v in os.environ.items() if k != "GH_TOKEN"}
    out = subprocess.run(["gh", "api", f"repos/{REPO}{path}"], capture_output=True, text=True, env=env, check=True)
    return json.loads(out.stdout)


def load(d, name):
    p = os.path.join(d, name)
    if not os.path.exists(p):
        return []
    with open(p) as f:
        data = json.load(f)
    return data.get("rows", data) if isinstance(data, dict) else data


def save(d, name, rows):
    with open(os.path.join(d, name), "w") as f:
        json.dump(rows, f, indent=1)


def main(d):
    os.makedirs(d, exist_ok=True)
    today = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%d")

    repo = api("")
    rels = api("/releases?per_page=100")
    views = api("/traffic/views")
    clones = api("/traffic/clones")
    refs = api("/traffic/popular/referrers")

    releases = []
    for r in rels:
        if r.get("draft"):
            continue
        releases.append({
            "version": r["tag_name"].lstrip("v"),
            "published": (r.get("published_at") or "")[:10],
            "downloads": sum(a["download_count"] for a in r.get("assets", [])),
        })
    total_downloads = sum(r["downloads"] for r in releases)

    # Daily snapshot (one row per day; a rerun the same day replaces that day's row).
    history = [h for h in load(d, "history.json") if h.get("day") != today]
    history.append({
        "day": today,
        "downloads": total_downloads,
        "stars": repo.get("stargazers_count", 0),
        "forks": repo.get("forks_count", 0),
        "watchers": repo.get("subscribers_count", 0),
        "open_issues": repo.get("open_issues_count", 0),
        "views_14d": views.get("count", 0),
        "visitors_14d": views.get("uniques", 0),
        "clones_14d": clones.get("count", 0),
    })
    history.sort(key=lambda h: h["day"])

    # Page views per day: GitHub keeps 14 days, so keep every day we have ever seen.
    vd = {v["day"]: v for v in load(d, "views_daily.json")}
    for v in views.get("views", []):
        day = v["timestamp"][:10]
        vd[day] = {"day": day, "views": v["count"], "visitors": v["uniques"]}
    views_daily = sorted(vd.values(), key=lambda v: v["day"])

    referrers = [{"source": r["referrer"], "views": r["count"], "visitors": r["uniques"]} for r in refs]

    save(d, "history.json", history)
    save(d, "views_daily.json", views_daily)
    save(d, "releases.json", releases)
    save(d, "referrers.json", referrers)
    print(json.dumps(history[-1]))


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else ".")
