"""Python ORT/reference 比較器的純數值契約測試。"""

from __future__ import annotations

import unittest

import numpy as np

from compare import compare_values


class CompareTests(unittest.TestCase):
    """驗證逐層數值比較不會忽略有效錯誤或誤判 masked sentinel。"""

    def test_relative_error_cannot_relax_absolute_limit(self) -> None:
        """大幅 raw logits 仍須遵守絕對誤差，不可由相對誤差放寬。"""
        result = compare_values("logits", np.array([100.0]), np.array([100.001]), 1e-4, 1e-4)
        self.assertFalse(result["passed"])
        self.assertEqual(result["firstFailureIndex"], [0])

    def test_invalid_or_unproven_tolerance_is_rejected(self) -> None:
        """非有限、負數或未附調查的放寬 tolerance 必須拒絕。"""
        for tolerance in (float("nan"), float("inf"), -1.0, 5e-4, 0.01):
            with self.subTest(tolerance=tolerance), self.assertRaises(ValueError):
                compare_values("logits", np.array([1.0]), np.array([1.0]), tolerance, 0.0)

    def test_masked_values_are_excluded_but_valid_values_are_checked(self) -> None:
        """padding marker 的任意值不影響結果，valid marker 超差仍失敗。"""
        result = compare_values(
            "logits",
            np.asarray([[1.0, 2.0, -10000.0]], dtype=np.float32),
            np.asarray([[1.00001, 2.00001, 123.0]], dtype=np.float32),
            absolute_tolerance=1e-4,
            relative_tolerance=1e-4,
            valid_mask=np.asarray([[True, True, False]]),
        )
        self.assertTrue(result["passed"])
        self.assertEqual(result["validCount"], 2)
        self.assertEqual(result["maskedCount"], 1)

    def test_non_finite_valid_value_is_a_failure(self) -> None:
        """有效輸出含 NaN/Inf 時即使差值不可計算也必須失敗。"""
        result = compare_values(
            "act_probs",
            np.asarray([[0.5, 0.5]], dtype=np.float32),
            np.asarray([[np.nan, 0.5]], dtype=np.float32),
            absolute_tolerance=1e-4,
            relative_tolerance=1e-4,
        )
        self.assertFalse(result["passed"])
        self.assertEqual(result["failureReason"], "non-finite-valid-value")


if __name__ == "__main__":
    unittest.main()
