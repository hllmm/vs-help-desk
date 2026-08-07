package main

import rego.v1

unrestricted_cidrs := {"0.0.0.0/0", "::/0"}

# These CIDRs are committed only as the current base-policy examples. They are
# not deployable mail-relay values; production relay CIDRs will be supplied by
# the explicit rendering introduced in Task 7.
example_relay_cidrs := {"10.20.30.0/24", "192.168.100.10/32"}

mail_ports := {25, 143, 465, 587, 993}

is_network_policy if {
  input.kind == "NetworkPolicy"
}

ip_block_cidrs contains cidr if {
  some ingress_rule in object.get(input.spec, "ingress", [])
  some peer in object.get(ingress_rule, "from", [])
  cidr := peer.ipBlock.cidr
}

ip_block_cidrs contains cidr if {
  some egress_rule in object.get(input.spec, "egress", [])
  some peer in object.get(egress_rule, "to", [])
  cidr := peer.ipBlock.cidr
}

deny contains msg if {
  is_network_policy
  some cidr in ip_block_cidrs
  cidr in unrestricted_cidrs
  msg := sprintf("unrestricted ipBlock CIDR %s is forbidden", [cidr])
}

deny contains msg if {
  is_network_policy
  some cidr in ip_block_cidrs
  cidr in example_relay_cidrs
  msg := sprintf("example relay CIDR %s is forbidden", [cidr])
}

is_web_policy if {
  input.spec.podSelector.matchLabels["app.kubernetes.io/name"] == "web"
}

has_namespace_selector_without_pod_selector(peers) if {
  some peer in peers
  peer.namespaceSelector
  not peer.podSelector
}

has_pod_selector_without_namespace_selector(peers) if {
  some peer in peers
  peer.podSelector
  not peer.namespaceSelector
}

deny contains "web ingress must combine namespaceSelector and podSelector in one peer" if {
  is_network_policy
  is_web_policy
  some ingress_rule in object.get(input.spec, "ingress", [])
  peers := object.get(ingress_rule, "from", [])
  has_namespace_selector_without_pod_selector(peers)
  has_pod_selector_without_namespace_selector(peers)
}

has_mail_port(ports) if {
  some port_spec in ports
  port_spec.port in mail_ports
}

deny contains "base api-allow may not use an ipBlock for SMTP or IMAP egress" if {
  is_network_policy
  input.metadata.name == "api-allow"
  some egress_rule in object.get(input.spec, "egress", [])
  has_mail_port(object.get(egress_rule, "ports", []))
  some peer in object.get(egress_rule, "to", [])
  peer.ipBlock.cidr
}
