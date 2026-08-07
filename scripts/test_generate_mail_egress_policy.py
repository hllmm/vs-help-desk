#!/usr/bin/env python3
import subprocess
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
GENERATOR = ROOT / "scripts" / "generate_mail_egress_policy.py"


class GenerateMailEgressPolicyTests(unittest.TestCase):
    def run_generator(self, smtp=None, imap=None):
        command = [sys.executable, str(GENERATOR)]
        if smtp is not None:
            command.extend(["--smtp-relay-cidrs", smtp])
        if imap is not None:
            command.extend(["--imap-relay-cidrs", imap])
        return subprocess.run(
            command,
            cwd=ROOT,
            capture_output=True,
            text=True,
        )

    def assert_rejected(self, expected_message, **values):
        result = self.run_generator(**values)
        self.assertNotEqual(result.returncode, 0, result.stderr)
        self.assertIn(expected_message, result.stderr)

    def test_requires_both_relay_lists(self):
        self.assert_rejected(
            "SMTP_RELAY_CIDRS must be set",
            imap="192.0.2.0/24",
        )
        self.assert_rejected(
            "IMAP_RELAY_CIDRS must be set",
            smtp="192.0.2.0/24",
        )

    def test_rejects_empty_cidr_entries(self):
        self.assert_rejected(
            "empty CIDR entry",
            smtp="192.0.2.0/24,",
            imap="198.51.100.0/24",
        )

    def test_rejects_invalid_cidr_entries(self):
        self.assert_rejected(
            "invalid CIDR",
            smtp="not-a-cidr",
            imap="198.51.100.0/24",
        )

    def test_rejects_equivalent_world_cidrs(self):
        for world_cidr in ("1.2.3.4/0", "2001:db8::/0"):
            with self.subTest(world_cidr=world_cidr):
                self.assert_rejected(
                    "world CIDR",
                    smtp=world_cidr,
                    imap="198.51.100.0/24",
                )

    def test_renders_separate_policy_with_canonical_cidrs_and_mail_ports(self):
        result = self.run_generator(
            smtp="203.0.113.1/24, 2001:0db8::/64",
            imap="192.0.2.10/32",
        )

        self.assertEqual(result.returncode, 0, result.stderr)
        policy = result.stdout
        self.assertIn("kind: NetworkPolicy", policy)
        self.assertIn("name: api-mail-egress", policy)
        self.assertIn("namespace: vshelpdesk", policy)
        self.assertIn("app.kubernetes.io/name: api", policy)
        self.assertIn("policyTypes:\n  - Egress", policy)
        self.assertIn("cidr: 203.0.113.0/24", policy)
        self.assertIn("cidr: 2001:db8::/64", policy)
        self.assertIn("cidr: 192.0.2.10/32", policy)
        for port in (25, 465, 587, 143, 993):
            self.assertRegex(policy, rf"port: {port}\n\s+protocol: TCP")


if __name__ == "__main__":
    unittest.main()
