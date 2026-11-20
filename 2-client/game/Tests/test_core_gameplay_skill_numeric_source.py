from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Assets/_Project/Scripts/Runtime/CoreGameplay.cs"


class CoreGameplaySkillNumericCultureTests(unittest.TestCase):
    def test_skill_values_are_read_as_json_numbers_not_culture_sensitive_text(self):
        source = SOURCE.read_text(encoding="utf-8")
        block = source.split("foreach (var skill in rows)", 1)[1].split(
            "GUILayout.BeginHorizontal();", 1
        )[0]
        self.assertIn("skill.Value.Type != JTokenType.Integer", block)
        self.assertIn("skill.Value.Type != JTokenType.Float", block)
        self.assertIn("skill.Value.Value<double>()", block)
        self.assertNotIn("skill.Value.ToString()", block)
        self.assertNotIn("double.TryParse", block)


if __name__ == "__main__":
    unittest.main()
