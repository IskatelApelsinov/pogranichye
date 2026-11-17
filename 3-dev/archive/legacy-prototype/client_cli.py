"""
Тестовый «толстый» клиент в терминале — доказательство, что сервер клиент-агностичен:
он говорит не с браузером, а по тому же WebSocket-протоколу (namerenia JSON + пуш
состояния). Это референс для будущего клиента на Godot/ПК: там та же схема.

    python3 client_cli.py                      # dev: входит гостем на localhost
    python3 client_cli.py --host H --port P --email e --password p

Команды в REPL: state | do <act> <q> | hunt | strike <q> | travel <i> | drink |
quest <id> | help <q> | save | quit. Это «тонкая» обёртка над сетью; вся логика —
на сервере, как и должно быть.
"""
from __future__ import annotations
import argparse, json, socket, base64, os, threading, time, urllib.request
from world import ws

RID = [0]


def http(base, path, data=None, cookie=None):
    req = urllib.request.Request(base + path,
        data=json.dumps(data).encode() if data is not None else None,
        headers={"Content-Type": "application/json", **({"Cookie": cookie} if cookie else {})},
        method="POST" if data is not None else "GET")
    r = urllib.request.urlopen(req)
    sc = r.headers.get("Set-Cookie")
    return json.load(r), (sc.split(";")[0] if sc else None)


def connect_ws(host, port, cookie):
    s = socket.create_connection((host, port))
    key = base64.b64encode(os.urandom(16)).decode()
    s.sendall((f"GET /ws HTTP/1.1\r\nHost: {host}:{port}\r\nUpgrade: websocket\r\n"
               f"Connection: Upgrade\r\nSec-WebSocket-Key: {key}\r\n"
               f"Sec-WebSocket-Version: 13\r\nCookie: {cookie}\r\n\r\n").encode())
    buf = b""
    while b"\r\n\r\n" not in buf:
        buf += s.recv(1)
    if b"101" not in buf.split(b"\r\n")[0]:
        raise RuntimeError("WS handshake failed: " + buf.decode(errors="replace"))
    return s


def send(sock, op, **kw):
    RID[0] += 1
    ws.send(sock, json.dumps(dict(op=op, rid=RID[0], **kw)))


def reader(sock):
    while True:
        fr = ws.recv(sock)
        if fr is None:
            print("\n[соединение закрыто]"); return
        op, data = fr
        if op != ws.OP_TEXT:
            continue
        try:
            m = json.loads(data.decode())
        except Exception:
            continue
        if m.get("type") == "ack":
            r = m.get("res", {})
            print("  <", r.get("msg", r))
        elif m.get("type") == "state":
            st = m["state"]
            me = st.get("me", {})
            if st.get("transit"):
                line = f"[в пути → {st['transit']['to']}, {st['transit']['left']}ч]"
            else:
                h = st.get("here", {})
                line = (f"[{h.get('name','?')} д{st.get('day')} {st.get('hour')}:00 "
                        f"{h.get('season','')} {h.get('weather','')}] "
                        f"★{me.get('reputation')} ⌾{me.get('coins')} уст{me.get('fatigue')}"
                        + (f" рядом:{','.join(h.get('players_here',[]))}" if h.get('players_here') else ""))
            print("\r" + line)
            if st.get("encounter"):
                e = st["encounter"]; print(f"  ⚔ {e['icon']} {e['name']} — бей: strike <q>")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=8090)
    ap.add_argument("--email", default=""); ap.add_argument("--password", default="")
    a = ap.parse_args()
    base = f"http://{a.host}:{a.port}"
    if a.email:
        r, cookie = http(base, "/auth/login", {"email": a.email, "password": a.password})
        if not r.get("ok"):
            r, cookie = http(base, "/auth/register", {"email": a.email, "password": a.password})
    else:
        r, cookie = http(base, "/auth/guest", {})
    if not r.get("ok"):
        print("вход не удался:", r); return
    print("вошёл как", r.get("status"), "— открываю мир…")
    sock = connect_ws(a.host, a.port, cookie)
    threading.Thread(target=reader, args=(sock,), daemon=True).start()
    print("Команды: do <act> <q> | hunt | strike <q> | travel <i> | drink | quest <id> | save | quit")
    try:
        while True:
            line = input().strip().split()
            if not line:
                continue
            cmd = line[0]
            if cmd == "quit":
                break
            elif cmd == "state":
                send(sock, "state")
            elif cmd == "do" and len(line) >= 3:
                send(sock, "do", activity=line[1], quality=float(line[2]))
            elif cmd == "strike":
                send(sock, "strike", quality=float(line[1]) if len(line) > 1 else 0.7)
            elif cmd == "travel":
                send(sock, "travel", to=int(line[1]))
            elif cmd == "quest":
                send(sock, "quest", qid=int(line[1]))
            elif cmd == "help":
                send(sock, "help", quality=float(line[1]) if len(line) > 1 else 0.7)
            elif cmd in ("hunt", "drink", "save"):
                send(sock, cmd)
            else:
                print("  ? неизвестная команда")
    except (EOFError, KeyboardInterrupt):
        pass
    finally:
        try:
            ws.send(sock, b"", ws.OP_CLOSE); sock.close()
        except OSError:
            pass
    print("пока!")


if __name__ == "__main__":
    main()
