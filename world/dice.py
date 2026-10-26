"""
КОСТИ / ядро случайности «Приграничья».

Это фундамент всей случайности в игре — и прототипа, и боевой версии. Всё, что
«бросает кубик» (бой, добыча, погода, события, азартные игры, спавн), берёт числа
отсюда, а не из голого random. Почему так:

  1. ДЕТЕРМИНИЗМ. При одном мировом сиде и той же последовательности событий — те же
     исходы. Это даёт воспроизводимость (баг повторяется), реплеи и честные тесты.
  2. НЕЗАВИСИМЫЕ ПОТОКИ. У каждой подсистемы/сущности/типа события — свой поток,
     выведенный хешем из (мировой_сид, имя_потока, ключ). Поэтому добавление новой
     системы НЕ сдвигает чужую случайность (раньше добавление черты меняло цифры
     всей симуляции — именно из-за общего потока).
  3. СЕРВЕР-АВТОРИТЕТ. Кубик бросает ТОЛЬКО сервер. Клиент шлёт намерение, сервер
     катит и присылает результат. Клиент не может «перекатать» — античит в основе.
  4. ЧЕСТНОСТЬ (доказуемая). Для азартных игр — схема commit-reveal: сервер заранее
     публикует хеш секрета, после игры раскрывает секрет, игрок проверяет, что кости
     не подменили задним числом. См. FairDice.
  5. ВОСПРОИЗВОДИМОСТЬ ПОСЛЕ РЕСТАРТА. Состояние потока — это одно 64-битное число,
     которое сохраняется вместе с миром; после перезапуска поток продолжается, а не
     катится заново.

Движок — splitmix64 (целочисленный, одинаковый на любой платформе и версии Python;
не зависит от реализации random). Сид потока — первые 8 байт SHA-256 от строки
(мировой_сид | ключ…). Так потоки статистически независимы и стабильны.
"""
from __future__ import annotations
import hashlib

_MASK = (1 << 64) - 1
_PHI = 0x9E3779B97F4A7C15


def _splitmix64(state: int):
    """Шаг splitmix64: возвращает (новое_состояние, 64-битный выход)."""
    state = (state + _PHI) & _MASK
    z = state
    z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & _MASK
    z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & _MASK
    z = (z ^ (z >> 31)) & _MASK
    return state, z


def seed64(world_seed, *key) -> int:
    """Вывести 64-битный сид потока из мирового сида и ключа (стабильный хеш)."""
    s = str(world_seed) + "|" + "|".join(str(k) for k in key)
    h = hashlib.sha256(s.encode("utf-8")).digest()
    return int.from_bytes(h[:8], "big")


class Stream:
    """Поток случайности. Состояние — одно целое; каждый бросок его двигает.
    Обычно получают через RngHub.stream(...), чтобы состояние сохранялось в мире."""

    def __init__(self, state: int, on_change=None):
        self._state = state & _MASK
        self._on_change = on_change        # колбэк (state) — чтобы хаб сохранил новое состояние

    # ── низкий уровень ──
    def _u64(self) -> int:
        self._state, z = _splitmix64(self._state)
        if self._on_change:
            self._on_change(self._state)
        return z

    def random(self) -> float:
        """Равномерно в [0, 1)."""
        return self._u64() / 18446744073709551616.0      # 2**64

    def randint(self, a: int, b: int) -> int:
        """Целое в [a, b] включительно."""
        if b < a:
            a, b = b, a
        return a + self._u64() % (b - a + 1)

    def uniform(self, a: float, b: float) -> float:
        return a + (b - a) * self.random()

    def randrange(self, start: int, stop: int | None = None) -> int:
        """Как random.randrange: [0,start) или [start,stop). Для drop-in совместимости."""
        if stop is None:
            lo, hi = 0, start
        else:
            lo, hi = start, stop
        return lo + self._u64() % max(1, (hi - lo))

    # ── кости ──
    def roll(self, sides: int = 6) -> int:
        """Один кубик d<sides>: 1..sides."""
        return 1 + self._u64() % max(1, sides)

    def roll_n(self, n: int, sides: int = 6) -> int:
        """Сумма n кубиков (например 3d6)."""
        return sum(self.roll(sides) for _ in range(max(0, n)))

    def roll_detail(self, n: int, sides: int = 6) -> list:
        """Список значений n кубиков (когда нужны отдельные кости — для показа)."""
        return [self.roll(sides) for _ in range(max(0, n))]

    def advantage(self, sides: int = 20) -> int:
        """Преимущество: бросить два, взять больший (как в настолках)."""
        return max(self.roll(sides), self.roll(sides))

    def disadvantage(self, sides: int = 20) -> int:
        """Помеха: бросить два, взять меньший."""
        return min(self.roll(sides), self.roll(sides))

    def exploding(self, sides: int = 6, cap: int = 20) -> int:
        """Взрывной кубик: на максимуме бросаем ещё и прибавляем (до cap бросков)."""
        total = 0
        for _ in range(cap):
            r = self.roll(sides)
            total += r
            if r != sides:
                break
        return total

    def pool(self, n: int, sides: int = 6, target: int = 4) -> int:
        """Пул кубиков: сколько из n выпало >= target (система успехов)."""
        return sum(1 for _ in range(max(0, n)) if self.roll(sides) >= target)

    # ── вероятности и выбор ──
    def chance(self, p: float) -> bool:
        """True с вероятностью p (0..1)."""
        return self.random() < p

    def choice(self, seq):
        seq = list(seq)
        if not seq:
            return None
        return seq[self._u64() % len(seq)]

    def weighted(self, pairs):
        """Взвешенный выбор: pairs = [(item, weight), ...]."""
        items = list(pairs)
        total = sum(max(0.0, w) for _, w in items)
        if total <= 0:
            return items[0][0] if items else None
        r = self.random() * total
        acc = 0.0
        for item, w in items:
            acc += max(0.0, w)
            if r < acc:
                return item
        return items[-1][0]

    def shuffle(self, lst: list) -> None:
        """Перемешать список на месте (Фишер–Йейтс на нашем потоке)."""
        for i in range(len(lst) - 1, 0, -1):
            j = self._u64() % (i + 1)
            lst[i], lst[j] = lst[j], lst[i]

    def d(self, expr: str) -> int:
        """Разобрать и бросить выражение вида '3d6+2', '1d20-1', 'd8'."""
        return roll_expr(self, expr)


# ── разбор нотации костей 'NdS±M' ──
def roll_expr(stream: "Stream", expr: str) -> int:
    expr = expr.replace(" ", "").lower()
    mod = 0
    for sign in ("+", "-"):
        if sign in expr[1:]:
            i = expr.index(sign, 1)
            mod = int(expr[i:])
            expr = expr[:i]
            break
    if "d" not in expr:
        return int(expr) + mod
    n_str, s_str = expr.split("d", 1)
    n = int(n_str) if n_str else 1
    s = int(s_str) if s_str else 6
    return stream.roll_n(n, s) + mod


class RngHub:
    """Хаб потоков случайности мира. Хранит по одному 64-битному состоянию на поток,
    чтобы потоки были независимы и переживали сохранение/перезапуск.

    hub.stream('combat', entity_id) и hub.stream('weather') — независимы:
    бросок в одном не влияет на другой."""

    def __init__(self, world_seed, states: dict | None = None):
        self.world_seed = world_seed
        self._states = dict(states or {})

    def _key(self, key) -> str:
        return "|".join(str(k) for k in key)

    def stream(self, *key) -> Stream:
        k = self._key(key)
        if k not in self._states:
            self._states[k] = seed64(self.world_seed, *key)
        def _save(state, _k=k):
            self._states[_k] = state
        return Stream(self._states[k], on_change=_save)

    def peek(self, *key) -> int:
        """Текущее состояние потока (для отладки/аудита), не двигая его."""
        k = self._key(key)
        return self._states.get(k, seed64(self.world_seed, *key))

    # ── одноразовый бросок без сохранения состояния (чисто из ключа) ──
    def once(self, *key) -> Stream:
        """Детерминированный поток ТОЛЬКО от ключа (не сохраняется, не двигает хаб).
        Для «чистых» бросков, однозначно привязанных к событию (день+тип+кто)."""
        return Stream(seed64(self.world_seed, *key))

    # ── персист ──
    def to_dict(self) -> dict:
        return {"world_seed": self.world_seed, "states": dict(self._states)}

    @classmethod
    def from_dict(cls, data: dict) -> "RngHub":
        return cls(data.get("world_seed", 1), data.get("states", {}))


# ── Доказуемо честные кости (commit-reveal) для азартных игр ──
class FairDice:
    """Честный кубик для игр на деньги. Сервер заранее фиксирует секрет и публикует
    его хеш (commit). Игра катится из секрета + публичных данных. После игры секрет
    раскрывается (reveal), и игрок может пересчитать броски и убедиться, что их не
    подменили. Так «казино» нельзя обвинить в жульничестве — всё проверяемо.

    Поток()  -> Stream для бросков игры.
    commit() -> хеш, который показывают игроку ДО игры.
    reveal() -> секрет, который показывают ПОСЛЕ; verify() проверяет пару.
    """

    def __init__(self, secret: str, public: str = ""):
        self.secret = str(secret)
        self.public = str(public)       # публичные входы (имена игроков, ставка, время)

    def commit(self) -> str:
        return hashlib.sha256(("commit|" + self.secret).encode()).hexdigest()

    def reveal(self) -> str:
        return self.secret

    def stream(self) -> Stream:
        return Stream(seed64(self.secret, "fair", self.public))

    @staticmethod
    def verify(secret: str, commit: str) -> bool:
        return hashlib.sha256(("commit|" + str(secret)).encode()).hexdigest() == commit
