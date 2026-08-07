package main

import rego.v1

allowed_repositories := {
    "api": "ghcr.io/vs-help-desk/api",
    "web": "ghcr.io/vs-help-desk/web",
}

mutable_tags := {"latest", "local", "stable"}

is_combined_input if {
    is_array(input)
}

manifest_documents := [document |
    is_combined_input
    some record in input
    document := record.contents
]

deployment_documents := [document |
    some document in manifest_documents
    document.kind == "Deployment"
]

workload_for_deployment(deployment) := workload if {
    template := object.get(object.get(deployment, "spec", {}), "template", {})
    metadata := object.get(template, "metadata", {})
    labels := object.get(metadata, "labels", {})
    workload := object.get(labels, "app.kubernetes.io/name", "")
    workload in {"api", "web"}
}

api_deployments := [deployment |
    some deployment in deployment_documents
    workload_for_deployment(deployment) == "api"
]

web_deployments := [deployment |
    some deployment in deployment_documents
    workload_for_deployment(deployment) == "web"
]

target_deployments := [deployment |
    some deployment in deployment_documents
    workload_for_deployment(deployment) in {"api", "web"}
]

deployment_containers(deployment) := containers if {
    template := object.get(object.get(deployment, "spec", {}), "template", {})
    pod_spec := object.get(template, "spec", {})
    containers := object.get(pod_spec, "containers", [])
}

is_allowed_immutable_image(workload, image) if {
    is_string(image)
    parts := split(image, "@")
    count(parts) == 2
    parts[0] == allowed_repositories[workload]
    regex.match("^sha256:[0-9a-f]{64}$", parts[1])
    not regex.match("^sha256:a{64}$", parts[1])
    not regex.match("^sha256:b{64}$", parts[1])
}

deny contains msg if {
    not is_combined_input
    msg := "production manifest must be evaluated as one combined manifest"
}

deny contains msg if {
    count(api_deployments) != 1
    msg := sprintf("exactly one API Deployment is required (found %d)", [count(api_deployments)])
}

deny contains msg if {
    count(web_deployments) != 1
    msg := sprintf("exactly one web Deployment is required (found %d)", [count(web_deployments)])
}

deny contains "API workload Deployment must be named api" if {
    some deployment in api_deployments
    object.get(object.get(deployment, "metadata", {}), "name", "") != "api"
}

deny contains "web workload Deployment must be named web" if {
    some deployment in web_deployments
    object.get(object.get(deployment, "metadata", {}), "name", "") != "web"
}

deny contains msg if {
    some deployment in target_deployments
    workload := workload_for_deployment(deployment)
    count(deployment_containers(deployment)) != 1
    msg := sprintf("%s Deployment must contain exactly one container", [workload])
}

deny contains msg if {
    some deployment in target_deployments
    workload := workload_for_deployment(deployment)
    containers := deployment_containers(deployment)
    count(containers) == 1
    container := containers[0]
    image := object.get(container, "image", "")
    not is_allowed_immutable_image(workload, image)
    msg := sprintf("%s Deployment image must use the exact allow-listed repository and a non-placeholder sha256 digest", [workload])
}

deny contains msg if {
    some deployment in target_deployments
    workload := workload_for_deployment(deployment)
    containers := deployment_containers(deployment)
    count(containers) == 1
    container := containers[0]
    image := object.get(container, "image", "")
    some tag in mutable_tags
    regex.match(sprintf(":%s$", [tag]), image)
    msg := sprintf("%s Deployment uses mutable image tag :%s", [workload, tag])
}
