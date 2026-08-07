package main

import rego.v1

allowed_repositories := data.production_image_allowlist

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
    containers := object.get(pod_spec, "containers", null)
}

deployment_init_containers(deployment) := containers if {
    template := object.get(object.get(deployment, "spec", {}), "template", {})
    pod_spec := object.get(template, "spec", {})
    containers := object.get(pod_spec, "initContainers", [])
}

pod_template_records contains record if {
    some document in manifest_documents
    document.kind in {"Deployment", "StatefulSet", "DaemonSet"}
    template := object.get(object.get(document, "spec", {}), "template", {})
    record := {"kind": document.kind, "template": template}
}

pod_template_records contains record if {
    some document in manifest_documents
    document.kind == "Job"
    template := object.get(object.get(document, "spec", {}), "template", {})
    record := {"kind": document.kind, "template": template}
}

pod_template_records contains record if {
    some document in manifest_documents
    document.kind == "CronJob"
    job_template := object.get(object.get(document, "spec", {}), "jobTemplate", {})
    template := object.get(object.get(job_template, "spec", {}), "template", {})
    record := {"kind": document.kind, "template": template}
}

workload_for_pod_template(template) := workload if {
    metadata := object.get(template, "metadata", {})
    labels := object.get(metadata, "labels", {})
    name := object.get(labels, "app.kubernetes.io/name", "")
    name in {"api", "web", "postgres"}
    workload := name
}

workload_for_pod_template(template) := "jobs" if {
    metadata := object.get(template, "metadata", {})
    labels := object.get(metadata, "labels", {})
    name := object.get(labels, "app.kubernetes.io/name", "")
    not name in {"api", "web", "postgres"}
    object.get(labels, "app.kubernetes.io/component", "") == "jobs"
}

is_allowed_immutable_image(workload, image) if {
    is_string(image)
    parts := split(image, "@")
    count(parts) == 2
    repository := object.get(allowed_repositories, workload, "")
    is_string(repository)
    repository != ""
    parts[0] == repository
    regex.match("^sha256:[0-9a-f]{64}$", parts[1])
    not regex.match("^sha256:a{64}$", parts[1])
    not regex.match("^sha256:b{64}$", parts[1])
}

has_allowed_repository(workload) if {
    repository := object.get(allowed_repositories, workload, "")
    is_string(repository)
    repository != ""
}

deny contains msg if {
    workload := ["api", "web", "postgres", "jobs"][_]
    not has_allowed_repository(workload)
    msg := sprintf("production image allowlist must provide a non-empty %s repository", [workload])
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
    containers := deployment_containers(deployment)
    not is_array(containers)
    msg := sprintf("%s Deployment spec.template.spec.containers must be an array", [workload])
}

deny contains msg if {
    some deployment in target_deployments
    workload := workload_for_deployment(deployment)
    containers := deployment_containers(deployment)
    is_array(containers)
    count(containers) != 1
    msg := sprintf("%s Deployment must contain exactly one regular container", [workload])
}

deny contains msg if {
    some deployment in target_deployments
    workload := workload_for_deployment(deployment)
    init_containers := deployment_init_containers(deployment)
    not is_array(init_containers)
    msg := sprintf("%s Deployment spec.template.spec.initContainers must be an array when present", [workload])
}

deny contains msg if {
    some deployment in target_deployments
    workload := workload_for_deployment(deployment)
    init_containers := deployment_init_containers(deployment)
    is_array(init_containers)
    some init_container in init_containers
    not is_object(init_container)
    msg := sprintf("%s Deployment initContainers entries must be objects with an image", [workload])
}

deny contains msg if {
    some deployment in target_deployments
    workload := workload_for_deployment(deployment)
    containers := deployment_containers(deployment)
    is_array(containers)
    some container in containers
    not is_object(container)
    msg := sprintf("%s Deployment containers entries must be objects with an image", [workload])
}

container_image_records contains record if {
    some deployment in target_deployments
    workload := workload_for_deployment(deployment)
    containers := deployment_containers(deployment)
    is_array(containers)
    some container in containers
    is_object(container)
    image := object.get(container, "image", null)
    record := {"image": image, "role": "container", "workload": workload}
}

container_image_records contains record if {
    some deployment in target_deployments
    workload := workload_for_deployment(deployment)
    init_containers := deployment_init_containers(deployment)
    is_array(init_containers)
    some init_container in init_containers
    is_object(init_container)
    image := object.get(init_container, "image", null)
    record := {"image": image, "role": "initContainer", "workload": workload}
}

deny contains msg if {
    some record in container_image_records
    not is_allowed_immutable_image(record.workload, record.image)
    msg := sprintf("%s Deployment %s image must use the exact allow-listed repository and a non-placeholder sha256 digest", [record.workload, record.role])
}

deny contains msg if {
    some record in container_image_records
    some tag in mutable_tags
    regex.match(sprintf(":%s$", [tag]), record.image)
    msg := sprintf("%s Deployment %s uses mutable image tag :%s", [record.workload, record.role, tag])
}

# Generalized pod template checks

deny contains msg if {
    some record in pod_template_records
    not workload_for_pod_template(record.template)
    msg := sprintf("%s pod template has unknown workload classification", [record.kind])
}

pod_template_containers(record) := containers if {
    pod_spec := object.get(record.template, "spec", {})
    containers := object.get(pod_spec, "containers", null)
}

pod_template_init_containers(record) := containers if {
    pod_spec := object.get(record.template, "spec", {})
    containers := object.get(pod_spec, "initContainers", [])
}

deny contains msg if {
    some record in pod_template_records
    workload := workload_for_pod_template(record.template)
    containers := pod_template_containers(record)
    not is_array(containers)
    msg := sprintf("%s %s spec.template.spec.containers must be an array", [workload, record.kind])
}

deny contains msg if {
    some record in pod_template_records
    workload := workload_for_pod_template(record.template)
    containers := pod_template_containers(record)
    is_array(containers)
    count(containers) != 1
    msg := sprintf("%s %s must contain exactly one regular container", [workload, record.kind])
}

deny contains msg if {
    some record in pod_template_records
    workload := workload_for_pod_template(record.template)
    init_containers := pod_template_init_containers(record)
    not is_array(init_containers)
    msg := sprintf("%s %s spec.template.spec.initContainers must be an array when present", [workload, record.kind])
}

deny contains msg if {
    some record in pod_template_records
    workload := workload_for_pod_template(record.template)
    init_containers := pod_template_init_containers(record)
    is_array(init_containers)
    some init_container in init_containers
    not is_object(init_container)
    msg := sprintf("%s %s initContainers entries must be objects with an image", [workload, record.kind])
}

deny contains msg if {
    some record in pod_template_records
    workload := workload_for_pod_template(record.template)
    containers := pod_template_containers(record)
    is_array(containers)
    some container in containers
    not is_object(container)
    msg := sprintf("%s %s containers entries must be objects with an image", [workload, record.kind])
}

pod_container_image_records contains record if {
    some pt in pod_template_records
    workload := workload_for_pod_template(pt.template)
    containers := pod_template_containers(pt)
    is_array(containers)
    some container in containers
    is_object(container)
    image := object.get(container, "image", null)
    record := {"image": image, "role": "container", "workload": workload, "kind": pt.kind}
}

pod_container_image_records contains record if {
    some pt in pod_template_records
    workload := workload_for_pod_template(pt.template)
    init_containers := pod_template_init_containers(pt)
    is_array(init_containers)
    some init_container in init_containers
    is_object(init_container)
    image := object.get(init_container, "image", null)
    record := {"image": image, "role": "initContainer", "workload": workload, "kind": pt.kind}
}

deny contains msg if {
    some record in pod_container_image_records
    not is_allowed_immutable_image(record.workload, record.image)
    msg := sprintf("%s %s %s image must use the exact allow-listed repository and a non-placeholder sha256 digest", [record.workload, record.kind, record.role])
}

deny contains msg if {
    some record in pod_container_image_records
    some tag in mutable_tags
    regex.match(sprintf(":%s$", [tag]), record.image)
    msg := sprintf("%s %s %s uses mutable image tag :%s", [record.workload, record.kind, record.role, tag])
}
