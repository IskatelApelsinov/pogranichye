from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
CLIENT_INPUT = ROOT / "Assets/_Project/Scripts/Runtime/ClientInput.cs"


class InputEventBudgetWiringChecks(unittest.TestCase):
    def test_budget_policy_runs_before_actions_are_enabled(self):
        source = CLIENT_INPUT.read_text(encoding="utf-8")
        policy_call = source.index("InputEventBudgetPolicy.Resolve")
        enable_actions = source.index("_actions.Enable();")
        self.assertLess(policy_call, enable_actions)

    def test_f3_diagnostics_report_the_effective_budget(self):
        source = CLIENT_INPUT.read_text(encoding="utf-8")
        self.assertIn("DescribeInputEventBudget()", source)
        self.assertIn('if (budget == 0) return "input budget: unlimited";', source)
        self.assertIn('"input budget: {0:0.#} MiB"', source)

    def test_unlimited_setting_is_not_replaced_by_a_finite_cap(self):
        source = CLIENT_INPUT.read_text(encoding="utf-8")
        self.assertIn("InputEventBudgetPolicy.Resolve(currentBudget, _maxInputEventBytes)", source)
        self.assertNotIn("maxEventBytesPerUpdate < maxInputEventBytes", source)


if __name__ == "__main__":
    unittest.main()
