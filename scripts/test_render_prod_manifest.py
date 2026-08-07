#!/usr/bin/env python3
import os
import subprocess
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
RENDERER = ROOT / "scripts" / "render-prod-manifest.sh"
API_IMAGE = "ghcr.io/vs-help-desk/api@sha256:" + "a" * 64
WEB_IMAGE = "ghcr.io/vs-help-desk/web@sha256:" + "b" * 64
MISSING = object()


class RenderProdManifestTests(unittest.TestCase):
    def run_renderer(self, mode=MISSING, smtp=MISSING, imap=MISSING):
        environment = os.environ.copy()
        environment.update({"API_IMAGE": API_IMAGE, "WEB_IMAGE": WEB_IMAGE})
        for name, value in (
            ("MAIL_EGRESS_MODE", mode),
            ("SMTP_RELAY_CIDRS", smtp),
            ("IMAP_RELAY_CIDRS", imap),
        ):
            if value is MISSING:
                environment.pop(name, None)
            else:
                environment[name] = value

        return subprocess.run(
            ["bash", str(RENDERER)],
            cwd=ROOT,
            env=environment,
            capture_output=True,
            text=True,
        )

    def test_requires_mail_egress_mode(self):
        result = self.run_renderer()

        self.assertNotEqual(result.returncode, 0, result.stderr)
        self.assertIn("MAIL_EGRESS_MODE", result.stderr)

    def test_rejects_unknown_mail_egress_mode(self):
        result = self.run_renderer(mode="automatic")

        self.assertNotEqual(result.returncode, 0, result.stderr)
        self.assertIn("MAIL_EGRESS_MODE", result.stderr)

    def test_disabled_mode_has_no_mail_egress_policy_and_needs_no_relays(self):
        result = self.run_renderer(
            mode="disabled",
            smtp="not-a-cidr",
            imap="",
        )

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("name: api-allow", result.stdout)
        self.assertNotIn("name: api-mail-egress", result.stdout)

    def test_enabled_mode_requires_each_relay_list(self):
        for missing_name, kwargs in (
            ("SMTP_RELAY_CIDRS", {"mode": "enabled", "imap": "192.0.2.0/24"}),
            ("IMAP_RELAY_CIDRS", {"mode": "enabled", "smtp": "198.51.100.0/24"}),
        ):
            with self.subTest(missing_name=missing_name):
                result = self.run_renderer(**kwargs)
                self.assertNotEqual(result.returncode, 0, result.stderr)
                self.assertIn(missing_name, result.stderr)

    def test_enabled_mode_rejects_invalid_empty_and_world_relays(self):
        for smtp in ("not-a-cidr", "198.51.100.0/24,", "1.2.3.4/0"):
            with self.subTest(smtp=smtp):
                result = self.run_renderer(
                    mode="enabled",
                    smtp=smtp,
                    imap="192.0.2.0/24",
                )
                self.assertNotEqual(result.returncode, 0, result.stderr)
                self.assertIn("CIDR", result.stderr)

    def test_enabled_mode_appends_separate_api_mail_egress_policy(self):
        result = self.run_renderer(
            mode="enabled",
            smtp="203.0.113.0/24,2001:db8::/64",
            imap="192.0.2.10/32",
        )

        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("name: api-mail-egress", result.stdout)
        policy_start = result.stdout.index("name: api-mail-egress")
        policy = result.stdout[policy_start:]
        self.assertIn("kind: NetworkPolicy", result.stdout)
        self.assertIn("name: api-allow", result.stdout)
        self.assertIn("namespace: vshelpdesk", policy)
        self.assertIn("app.kubernetes.io/name: api", policy)
        self.assertIn("policyTypes:\n  - Egress", policy)
        for cidr in ("203.0.113.0/24", "2001:db8::/64", "192.0.2.10/32"):
            self.assertIn(f"cidr: {cidr}", policy)
        for port in (25, 465, 587, 143, 993):
            self.assertIn(f"port: {port}", policy)


if __name__ == "__main__":
    unittest.main()
