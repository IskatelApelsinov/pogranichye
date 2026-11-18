import re
import unittest
from pathlib import Path


GAME_ROOT = Path(__file__).resolve().parents[1]
CYRILLIC = re.compile(r"[А-Яа-яЁё]")

CALLS = {
    "Assets/_Project/Scripts/Networking/HttpAuthClient.cs": (
        r"\bthrow\s+new\s+[A-Za-z_][A-Za-z0-9_]*\s*\(",
    ),
    "Assets/_Project/Scripts/Diagnostics/ClientDiagnosticDumps.cs": (
        r"\bthrow\s+new\s+[A-Za-z_][A-Za-z0-9_]*\s*\(",
        r"\bDebug\.LogWarning\s*\(",
    ),
    "Assets/_Project/Scripts/Runtime/ClientInput.cs": (
        r"\bDebug\.LogWarning\s*\(",
    ),
    "Assets/_Project/Scripts/Presentation/SettlementLayoutCache.cs": (
        r"\bDebug\.LogWarning\s*\(",
    ),
}


def call_expressions(source, pattern):
    for match in re.finditer(pattern, source):
        opening = source.find("(", match.start())
        depth = 0
        in_string = False
        escaped = False
        for index in range(opening, len(source)):
            char = source[index]
            if in_string:
                if escaped:
                    escaped = False
                elif char == "\\":
                    escaped = True
                elif char == '"':
                    in_string = False
                continue
            if char == '"':
                in_string = True
            elif char == "(":
                depth += 1
            elif char == ")":
                depth -= 1
                if depth == 0:
                    yield source[match.start():index + 1]
                    break


class EnglishClientDiagnosticsTests(unittest.TestCase):
    def test_technical_exception_and_warning_messages_are_english(self):
        checked = 0
        for relative_path, patterns in CALLS.items():
            source = (GAME_ROOT / relative_path).read_text(encoding="utf-8")
            for pattern in patterns:
                for expression in call_expressions(source, pattern):
                    checked += 1
                    self.assertIsNone(
                        CYRILLIC.search(expression),
                        msg=f"Technical diagnostic must use English: {relative_path}: {expression}",
                    )
        self.assertGreaterEqual(checked, 30, "Expected diagnostic scan to cover the current network and runtime calls")


if __name__ == "__main__":
    unittest.main()
