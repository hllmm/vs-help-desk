package main

import rego.v1

# These CIDRs are committed only as the current base-policy examples. They are
# not deployable mail-relay values; production relay CIDRs will be supplied by
# the explicit rendering introduced in Task 7.
example_relay_cidrs := {"10.20.30.0/24", "192.168.100.10/32"}

mail_ports := {25, 143, 465, 587, 993}
mail_port_names := {"imap", "imaps", "smtp", "smtps", "submission"}
generated_mail_policy_name := "api-mail-egress"
generated_mail_policy_provenance_key := "vshelpdesk.io/policy-provenance"
generated_mail_policy_provenance_value := "task-7-mail-egress-generator"

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

is_generated_mail_policy if {
  is_network_policy
  metadata := object.get(input, "metadata", {})
  metadata.name == generated_mail_policy_name
  annotations := object.get(metadata, "annotations", {})
  object.get(annotations, generated_mail_policy_provenance_key, "") == generated_mail_policy_provenance_value
}

deny contains "SMTP/IMAP egress with an ipBlock is forbidden" if {
  is_network_policy
  not is_generated_mail_policy
  some egress_rule in object.get(input.spec, "egress", [])
  has_mail_port_overlap(object.get(egress_rule, "ports", []))
  some peer in object.get(egress_rule, "to", [])
  peer.ipBlock.cidr
}

is_combined_input if {
  is_array(input)
}

combined_network_policies contains policy if {
  is_combined_input
  some record in input
  policy := record.contents
  policy.kind == "NetworkPolicy"
}

ingress_nginx_namespace_labels := {"kubernetes.io/metadata.name": "ingress-nginx"}
ingress_nginx_pod_labels := {"app.kubernetes.io/name": "ingress-nginx"}
kube_dns_namespace_labels := {"kubernetes.io/metadata.name": "kube-system"}
kube_dns_pod_labels := {"k8s-app": "kube-dns"}

has_exact_web_ingress_peer if {
  some policy in combined_network_policies
  policy.metadata.name == "web-allow-ingress"
  some ingress_rule in object.get(policy.spec, "ingress", [])
  some peer in object.get(ingress_rule, "from", [])
  namespace_selector := object.get(peer, "namespaceSelector", {})
  pod_selector := object.get(peer, "podSelector", {})
  object.get(namespace_selector, "matchLabels", {}) == ingress_nginx_namespace_labels
  object.get(pod_selector, "matchLabels", {}) == ingress_nginx_pod_labels
}

deny contains "rendered web ingress peer must use exact ingress-nginx selectors" if {
  is_combined_input
  not has_exact_web_ingress_peer
}

deny contains "rendered kube-dns peer must use only its real label" if {
  is_combined_input
  some policy in combined_network_policies
  some egress_rule in object.get(policy.spec, "egress", [])
  some peer in object.get(egress_rule, "to", [])
  namespace_selector := object.get(peer, "namespaceSelector", {})
  namespace_labels := object.get(namespace_selector, "matchLabels", {})
  namespace_labels == kube_dns_namespace_labels
  pod_selector := object.get(peer, "podSelector", {})
  pod_labels := object.get(pod_selector, "matchLabels", {})
  object.get(pod_labels, "k8s-app", "") == "kube-dns"
  pod_labels != kube_dns_pod_labels
}

has_exact_kube_dns_peer if {
  some policy in combined_network_policies
  some egress_rule in object.get(policy.spec, "egress", [])
  some peer in object.get(egress_rule, "to", [])
  namespace_selector := object.get(peer, "namespaceSelector", {})
  pod_selector := object.get(peer, "podSelector", {})
  object.get(namespace_selector, "matchLabels", {}) == kube_dns_namespace_labels
  object.get(pod_selector, "matchLabels", {}) == kube_dns_pod_labels
}

deny contains "rendered base must include an exact kube-dns peer" if {
  is_combined_input
  not has_exact_kube_dns_peer
}

jobs_policy_exists if {
  some policy in combined_network_policies
  policy.metadata.name == "jobs-allow-egress"
}

jobs_policy_has_api_egress if {
  some policy in combined_network_policies
  policy.metadata.name == "jobs-allow-egress"
  spec := object.get(policy, "spec", {})
  object.get(object.get(spec, "podSelector", {}), "matchLabels", {}) == {
    "app.kubernetes.io/component": "jobs",
  }
  "Egress" in object.get(spec, "policyTypes", [])
  some egress_rule in object.get(spec, "egress", [])
  some peer in object.get(egress_rule, "to", [])
  not peer.namespaceSelector
  object.get(object.get(peer, "podSelector", {}), "matchLabels", {}) == {
    "app.kubernetes.io/name": "api",
  }
  some port in object.get(egress_rule, "ports", [])
  port.port == 8080
  upper(object.get(port, "protocol", "TCP")) == "TCP"
}

jobs_policy_has_dns_egress if {
  some policy in combined_network_policies
  policy.metadata.name == "jobs-allow-egress"
  some egress_rule in object.get(policy.spec, "egress", [])
  some peer in object.get(egress_rule, "to", [])
  namespace_selector := object.get(peer, "namespaceSelector", {})
  pod_selector := object.get(peer, "podSelector", {})
  object.get(namespace_selector, "matchLabels", {}) == kube_dns_namespace_labels
  object.get(pod_selector, "matchLabels", {}) == kube_dns_pod_labels
  some udp_port in object.get(egress_rule, "ports", [])
  udp_port.port == 53
  upper(object.get(udp_port, "protocol", "TCP")) == "UDP"
  some tcp_port in object.get(egress_rule, "ports", [])
  tcp_port.port == 53
  upper(object.get(tcp_port, "protocol", "TCP")) == "TCP"
}

deny contains "rendered base must include jobs egress NetworkPolicy" if {
  is_combined_input
  not jobs_policy_exists
}

deny contains "jobs egress must allow same-namespace API TCP 8080" if {
  is_combined_input
  not jobs_policy_has_api_egress
}

deny contains "jobs egress must allow kube-dns UDP and TCP 53" if {
  is_combined_input
  not jobs_policy_has_dns_egress
}
