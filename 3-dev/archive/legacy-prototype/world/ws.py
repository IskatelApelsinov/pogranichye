"""
Минимальный WebSocket (RFC 6455) на stdlib — без внешних зависимостей.
Нам нужны текстовые кадры (JSON) в обе стороны, ping/pong и close. Этого хватает,
чтобы заменить HTTP-опрос живым каналом: сервер сам шлёт состояние при изменении,
клиент шлёт намерения — без заголовков и переустановки соединения на каждый тик.

Серверные кадры идут без маски, клиентские приходят с маской (так требует RFC).
"""
from __future__ import annotations
import base64, hashlib

_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"
OP_TEXT, OP_CLOSE, OP_PING, OP_PONG = 0x1, 0x8, 0x9, 0xA
MAX_FRAME = 1 << 20          # 1 МБ — защита от кадра-гиганта (исчерпание памяти)


def accept_key(key: str) -> str:
    """Ответ на Sec-WebSocket-Key для рукопожатия."""
    return base64.b64encode(hashlib.sha1((key + _GUID).encode()).digest()).decode()


def _read_exact(sock, n: int) -> bytes | None:
    buf = b""
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk:
            return None
        buf += chunk
    return buf


def send(sock, data, opcode: int = OP_TEXT) -> None:
    """Отправить кадр (текст по умолчанию). Сервер не маскирует."""
    payload = data.encode("utf-8") if isinstance(data, str) else data
    hdr = bytearray([0x80 | opcode])          # FIN=1 + opcode
    n = len(payload)
    if n < 126:
        hdr.append(n)
    elif n < 65536:
        hdr.append(126); hdr += n.to_bytes(2, "big")
    else:
        hdr.append(127); hdr += n.to_bytes(8, "big")
    sock.sendall(bytes(hdr) + payload)


def recv(sock):
    """Прочитать один кадр → (opcode, bytes) или None при закрытии/ошибке."""
    h = _read_exact(sock, 2)
    if not h:
        return None
    opcode = h[0] & 0x0F
    masked = h[1] & 0x80
    ln = h[1] & 0x7F
    if ln == 126:
        ext = _read_exact(sock, 2)
        if ext is None:
            return None
        ln = int.from_bytes(ext, "big")
    elif ln == 127:
        ext = _read_exact(sock, 8)
        if ext is None:
            return None
        ln = int.from_bytes(ext, "big")
    if ln > MAX_FRAME:
        return None                       # слишком большой кадр — рвём соединение
    mask = _read_exact(sock, 4) if masked else b""
    if masked and mask is None:
        return None
    payload = _read_exact(sock, ln) if ln else b""
    if payload is None:
        return None
    if masked:
        payload = bytes(payload[i] ^ mask[i % 4] for i in range(ln))
    return opcode, payload
