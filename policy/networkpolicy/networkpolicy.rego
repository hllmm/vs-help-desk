package main

import rego.v1

# These CIDRs are committed only as the current base-policy examples. They are
# not deployable mail-relay values; production relay CIDRs will be supplied by
# the explicit rendering introduced in Task 7.
example_relay_cidrs := {"10.20.30.0/24", "192.168.100.10/32"}

mail_ports := {25, 143, 465, 587, 993}
mail_port_names := {"imap", "imaps", "smtp", "smtps", "submission"}

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
  is_world_cidr(cidr)
  msg := sprintf("unrestricted ipBlock CIDR %s is forbidden", [cidr])
}

is_world_cidr(cidr) if {
  is_string(cidr)
  net.cidr_is_valid(cidr)
  parts := split(cidr, "/")
  count(parts) == 2
  to_number(parts[1]) == 0
}

deny contains msg if {
  is_network_policy
  some cidr in ip_block_cidrs
  cidr in example_relay_cidrs
  msg := sprintf("example relay CIDR %s is forbidden", [cidr])
}

is_web_policy if {
  input.metadata.name == "web-allow-ingress"
}

is_web_policy if {
  input.spec.podSelector.matchLabels["app.kubernetes.io/name"] == "web"
}

has_namespace_selector_without_pod_selector(ingress_rules) if {
  some ingress_rule in ingress_rules
  some peer in object.get(ingress_rule, "from", [])
  peer.namespaceSelector
  not peer.podSelector
}

has_pod_selector_without_namespace_selector(ingress_rules) if {
  some ingress_rule in ingress_rules
  some peer in object.get(ingress_rule, "from", [])
  peer.podSelector
  not peer.namespaceSelector
}

deny contains "web ingress must combine namespaceSelector and podSelector in one peer" if {
  is_network_policy
  is_web_policy
  ingress_rules := object.get(input.spec, "ingress", [])
  has_namespace_selector_without_pod_selector(ingress_rules)
  has_pod_selector_without_namespace_selector(ingress_rules)
}

port_number(port) := port if {
  is_number(port)
}

port_number(port) := to_number(port) if {
  is_string(port)
}

is_tcp_port(port_spec) if {
  upper(object.get(port_spec, "protocol", "TCP")) == "TCP"
}

has_mail_port_overlap(ports) if {
  some port_spec in ports
  is_tcp_port(port_spec)
  start := port_number(port_spec.port)
  end := port_number(object.get(port_spec, "endPort", port_spec.port))
  some mail_port in mail_ports
  start <= mail_port
  mail_port <= end
}

has_mail_port_overlap(ports) if {
  some port_spec in ports
  is_tcp_port(port_spec)
  not port_spec.endPort
  port_spec.port in mail_port_names
}

deny contains "SMTP/IMAP egress with an ipBlock is forbidden" if {
  is_network_policy
  some egress_rule in object.get(input.spec, "egress", [])
  has_mail_port_overlap(object.get(egress_rule, "ports", []))
  some peer in object.get(egress_rule, "to", [])
  peer.ipBlock.cidr
}
