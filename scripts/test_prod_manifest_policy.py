#!/usr/bin/env python3
import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
CHECKER = ROOT / "scripts" / "check-prod-manifest.sh"
FIXTURES = ROOT / "policy" / "production" / "fixtures"
PROD_OVERLAY = ROOT / "deploy" / "k8s" / "overlays" / "prod"


class ProductionManifestPolicyTests(unittest.TestCase):
    def run_checker(self, manifest=None, fixture=None):
        command = ["bash", str(CHECKER)]
        if fixture is not None:
            command.append(str(FIXTURES / fixture))

        if manifest is None:
            return subprocess.run(command, cwd=ROOT, capture_output=True, text=True)

        return subprocess.run(
            command,
            cwd=ROOT,
            input=manifest,
            capture_output=True,
            text=True,
        )

    def test_accepts_a_manifest_with_one_valid_api_and_web_deployment(self):
        result = self.run_checker(fixture="valid-api-web.yaml")

        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_rejects_the_unrendered_production_overlay(self):
        rendered = subprocess.run(
            ["kubectl", "kustomize", str(PROD_OVERLAY)],
            cwd=ROOT,
            capture_output=True,
            text=True,
        )
        self.assertEqual(rendered.returncode, 0, rendered.stdout + rendered.stderr)

        result = self.run_checker(manifest=rendered.stdout)

        self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)

    def test_rejects_structurally_invalid_or_non_immutable_production_manifests(self):
        invalid_fixtures = (
            "unsafe-missing-api.yaml",
            "unsafe-missing-web.yaml",
            "unsafe-duplicate-api.yaml",
            "unsafe-wrong-kind-api.yaml",
            "unsafe-wrong-kind-web.yaml",
            "unsafe-missing-api-image.yaml",
            "unsafe-duplicate-web.yaml",
            "unsafe-disallowed-api-repository.yaml",
            "unsafe-latest-tag.yaml",
            "unsafe-local-tag.yaml",
            "unsafe-stable-tag.yaml",
            "unsafe-fake-a-digest.yaml",
            "unsafe-fake-b-digest.yaml",
        )

        for fixture in invalid_fixtures:
            with self.subTest(fixture=fixture):
                result = self.run_checker(fixture=fixture)

                self.assertNotEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
