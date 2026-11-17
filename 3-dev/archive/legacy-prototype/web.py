"""
Окно в мир — веб-просмотр. Без зависимостей (stdlib). Тонкая оболочка над world/.
    python3 web.py   →  http://127.0.0.1:8080
Сервер мира крутится в фоне, страница лишь показывает состояние и сама обновляется.
Темп: 1 реальная секунда = 1 игровой час.
"""
from __future__ import annotations
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import threading
import time
import html

import world as W

DEFAULT_SPEED = 1.0
PORT = 8080

_lock = threading.Lock()
_world = W.make_world()
_clock = W.WorldClock()
_engine = W.build_engine(_world, _clock, seed=1)
_running = True


def _loop():
    while _running:
        with _lock:
            _engine.step_hour()
        time.sleep(1.0 / DEFAULT_SPEED)


def _bar(val, lo_good=False):
    v = max(0.0, min(100.0, val))
    good = v < 50 if lo_good else v > 50
    color = "#6ab04c" if good else ("#f0932b" if 25 < v < 75 else "#eb4d4b")
    return (f'<div class="bar"><div class="fill" style="width:{v:.0f}%;'
            f'background:{color}"></div></div>')


def page():
    with _lock:
        w, clock = _world, _clock
        health = w.health()
        fd = w.food_days()
        store = dict(w.store)
        residents = [(c.name, c.role, c.last_act, c.hunger, c.fatigue, c.alive)
                     for c in w.residents]
        news = list(w.news[-12:])[::-1]
        chron = list(w.chronicle[-10:])[::-1]
        graves = w.graves
        tavern_now = list(w.tavern_tonight)
        mead = w.store.get("mead", 0)
        alive = len(w.living())
        mods = ", ".join(s.name for s in _engine.systems)

    h_color = "#6ab04c" if health > 60 else ("#f0932b" if health > 25 else "#eb4d4b")
    rows = ""
    for name, role, act, hunger, fat, al in residents:
        if not al:
            rows += (f'<tr class="dead"><td>{html.escape(name)}</td><td>{role}</td>'
                     f'<td>почил</td><td>—</td><td>—</td></tr>')
            continue
        rows += (f"<tr><td>{html.escape(name)}</td><td>{role}</td>"
                 f"<td>{html.escape(act)}</td>"
                 f"<td>{_bar(hunger, True)}</td><td>{_bar(fat, True)}</td></tr>")
    news_html = "".join(f"<li>{html.escape(n)}</li>" for n in news) or "<li>пока тихо…</li>"
    chron_html = "".join(f"<li>{html.escape(n)}</li>" for n in chron) or "<li>история ещё не началась…</li>"
    store_html = " · ".join(f"{k}: {v:.0f}" for k, v in store.items())
    if tavern_now:
        tav = ("В таверне сейчас, за медовухой: "
               + ", ".join(html.escape(n) for n in tavern_now)
               + f" <span class='sub'>· медовухи {mead:.0f}</span>")
    else:
        tav = f"<span class='sub'>Таверна тиха · медовухи {mead:.0f}</span>"

    return f"""<!doctype html><html lang="ru"><head><meta charset="utf-8">
<meta http-equiv="refresh" content="2"><title>{html.escape(w.name)}</title>
<style>
 body{{background:#1e1e24;color:#e8e6e3;font:15px/1.5 system-ui,sans-serif;max-width:760px;margin:24px auto;padding:0 16px}}
 h1{{font-size:22px;margin:0 0 2px}} .sub{{color:#9b9a97;margin:0 0 18px}}
 .card{{background:#27272f;border:1px solid #343440;border-radius:12px;padding:14px 16px;margin:12px 0}}
 .big{{font-size:28px;font-weight:700;color:{h_color}}}
 table{{width:100%;border-collapse:collapse}} td,th{{text-align:left;padding:5px 8px}}
 th{{color:#9b9a97;font-weight:600;font-size:12px;text-transform:uppercase}}
 tr{{border-top:1px solid #343440}} .dead{{color:#6b6a67;font-style:italic}}
 .bar{{background:#343440;border-radius:5px;height:9px;width:90px;overflow:hidden}} .fill{{height:100%}}
 ul{{margin:6px 0 0;padding-left:18px}} li{{margin:3px 0;color:#cfcdc9}}
</style></head><body>
 <h1>{html.escape(w.name)}</h1>
 <p class="sub">День {clock.day}, {clock.hour:02d}:00 · живых {alive}/{len(residents)} · модули: {mods}</p>
 <div class="card"><div class="big">здоровье {health}</div>
  <div class="sub">еды на {fd:.1f} дней · склад: {store_html} · топор-износ {w.axe_wear:.0f}</div></div>
 <div class="card"><table>
  <tr><th>житель</th><th>дело</th><th>занят</th><th>голод</th><th>усталость</th></tr>{rows}</table></div>
 <div class="card">🍯 {tav}</div>
 <div class="card"><b>Что говорят в деревне</b><ul>{news_html}</ul></div>
 <div class="card"><b>Память деревни</b> <span class="sub">· могил: {graves}</span>
  <ul>{chron_html}</ul></div>
 <p class="sub">обновляется каждые 2 с · мир тикает {DEFAULT_SPEED:.0f} ч/с</p>
</body></html>"""


class Handler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path not in ("/", "/index.html"):
            self.send_error(404); return
        body = page().encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)
    def log_message(self, *a): pass


if __name__ == "__main__":
    threading.Thread(target=_loop, daemon=True).start()
    print(f"Окно в мир: http://127.0.0.1:{PORT}  (Ctrl+C чтобы остановить)")
    try:
        ThreadingHTTPServer(("127.0.0.1", PORT), Handler).serve_forever()
    except KeyboardInterrupt:
        _running = False
        print("\nмир остановлен.")
