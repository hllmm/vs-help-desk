#!/usr/bin/env python3
"""Validate explicit relay CIDRs and render the production mail policy."""

import argparse
import ipaddress
import sys


def parse_cidr_list(value, variable_name):
    if value is None or not value.strip():
        raise ValueError(f"{variable_name} must be set to a non-empty comma-separated list of CIDRs")

    networks = []
    for position, raw_entry in enumerate(value.split(","), start=1):
        entry = raw_entry.strip()
        if not entry:
            raise ValueError(f"{variable_name} contains an empty CIDR entry at position {position}")

        try:
            network = ipaddress.ip_network(entry, strict=False)
        except ValueError as error:
            raise ValueError(f"{variable_name} contains invalid CIDR {entry!r}") from error

        if network.prefixlen == 0:
            raise ValueError(f"{variable_name} contains forbidden world CIDR {entry!r}")

        networks.append(str(network))

    return networks


def render_policy(smtp_cidrs, imap_cidrs):
    lines = [
        "---",
        "apiVersion: networking.k8s.io/v1",
        "kind: NetworkPolicy",
        "metadata:",
        "  name: api-mail-egress",
        "  namespace: vshelpdesk",
        "  labels:",
        "    app.kubernetes.io/part-of: vs-help-desk",
        "spec:",
        "  podSelector:",
        "    matchLabels:",
        "      app.kubernetes.io/name: api",
        "  policyTypes:",
        "  - Egress",
        "  egress:",
    ]

    append_egress_rule(lines, smtp_cidrs, (25, 465, 587))
    append_egress_rule(lines, imap_cidrs, (143, 993))
    return "\n".join(lines) + "\n"


def append_egress_rule(lines, cidrs, ports):
    lines.append("  - to:")
    for cidr in cidrs:
        lines.extend(
            [
                "      - ipBlock:",
                f"          cidr: {cidr}",
            ]
        )
    lines.append("    ports:")
    for port in ports:
        lines.extend(
            [
                f"      - port: {port}",
                "        protocol: TCP",
            ]
        )


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--smtp-relay-cidrs")
    parser.add_argument("--imap-relay-cidrs")
    args = parser.parse_args(argv)

    try:
        smtp_cidrs = parse_cidr_list(args.smtp_relay_cidrs, "SMTP_RELAY_CIDRS")
        imap_cidrs = parse_cidr_list(args.imap_relay_cidrs, "IMAP_RELAY_CIDRS")
    except ValueError as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1

    sys.stdout.write(render_policy(smtp_cidrs, imap_cidrs))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
