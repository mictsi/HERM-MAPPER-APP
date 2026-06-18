#!/usr/bin/env sh
set -eu

image_name='herm-mapper-app'
tag='local'
requested_platforms=''
output_directory=''
progress='auto'
load_image=false
no_cache=false
pull=false

usage() {
    cat <<'EOF'
Usage: docker/Build-DockerImages.sh [options]

Build loadable Docker image archives for the HERM Mapper compose files.

Options:
  --image-name VALUE        Docker image name. Default: herm-mapper-app
  --tag VALUE               Docker image tag. Default: local
  --platform VALUE          Platform to build. May be repeated or comma-separated.
                            Supported: linux/amd64, linux/arm64, x64, arm64, all
  --output-directory VALUE  Directory for .tar image archives. Default: docker/images
  --progress VALUE          Docker build progress mode. Default: auto
  --load                    Load the archive matching the local Docker engine.
  --no-cache                Build without cache.
  --pull                    Always attempt to pull newer base images.
  -h, --help                Show this help.

Examples:
  docker/Build-DockerImages.sh
  docker/Build-DockerImages.sh --load
  docker/Build-DockerImages.sh --platform linux/amd64 --tag local
EOF
}

die() {
    printf '%s\n' "$*" >&2
    exit 1
}

script_dir() {
    current_script=$0
    case "$current_script" in
        */*) script_path=$current_script ;;
        *) script_path=./$current_script ;;
    esac

    cd "$(dirname "$script_path")" && pwd -P
}

get_repo_root() {
    current=$1

    while :; do
        if [ -f "$current/HERM-MAPPER-APP.sln" ]; then
            printf '%s\n' "$current"
            return 0
        fi

        parent=$(dirname "$current")
        if [ -z "$parent" ] || [ "$parent" = "$current" ]; then
            die "Could not locate repository root from '$1'."
        fi

        current=$parent
    done
}

assert_docker_cli() {
    if ! command -v docker >/dev/null 2>&1; then
        die 'Docker CLI was not found on PATH. Install Docker Desktop or add docker to PATH before running this script.'
    fi
}

normalize_platform() {
    value=$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')

    case "$value" in
        all) printf '%s\n' 'linux/amd64' 'linux/arm64' ;;
        amd64 | x64 | x86_64 | linux/x64 | linux/x86_64 | linux/amd64) printf '%s\n' 'linux/amd64' ;;
        arm64 | aarch64 | linux/aarch64 | linux/arm64) printf '%s\n' 'linux/arm64' ;;
        *) die "Unsupported platform '$1'. Use linux/amd64, linux/arm64, x64, arm64, or all." ;;
    esac
}

append_platform() {
    raw_value=$1
    for platform_value in $(printf '%s' "$raw_value" | tr ',' ' '); do
        for normalized_platform in $(normalize_platform "$platform_value"); do
            case " $platforms " in
                *" $normalized_platform "*) ;;
                *) platforms="${platforms}${platforms:+ }$normalized_platform" ;;
            esac
        done
    done
}

platform_tag_suffix() {
    case "$1" in
        linux/amd64) printf '%s\n' 'amd64' ;;
        linux/arm64) printf '%s\n' 'arm64' ;;
        *) die "Unsupported Docker platform '$1'." ;;
    esac
}

sanitize_name() {
    printf '%s' "$1" | sed 's#[\\/:*?"<>|][\\/:*?"<>|]*#-#g'
}

archive_name() {
    docker_platform=$1
    image=$2
    image_tag=$3

    safe_image=$(sanitize_name "$image")
    safe_tag=$(sanitize_name "$image_tag")
    safe_platform=$(printf '%s' "$docker_platform" | tr '/' '-')

    printf '%s_%s_%s.tar\n' "$safe_image" "$safe_tag" "$safe_platform"
}

resolve_directory() {
    target_directory=$1
    mkdir -p "$target_directory"
    cd "$target_directory" && pwd -P
}

docker_server_platform() {
    server_platform=$(docker version --format '{{.Server.Os}}/{{.Server.Arch}}' | sed -n '1p')
    if [ -z "$server_platform" ]; then
        die 'Could not detect the Docker server platform.'
    fi

    normalize_platform "$server_platform" | sed -n '1p'
}

while [ "$#" -gt 0 ]; do
    case "$1" in
        --image-name)
            [ "$#" -ge 2 ] || die '--image-name requires a value.'
            image_name=$2
            shift 2
            ;;
        --image-name=*)
            image_name=${1#*=}
            shift
            ;;
        --tag)
            [ "$#" -ge 2 ] || die '--tag requires a value.'
            tag=$2
            shift 2
            ;;
        --tag=*)
            tag=${1#*=}
            shift
            ;;
        --platform)
            [ "$#" -ge 2 ] || die '--platform requires a value.'
            requested_platforms="${requested_platforms}${requested_platforms:+ }$2"
            shift 2
            ;;
        --platform=*)
            requested_platforms="${requested_platforms}${requested_platforms:+ }${1#*=}"
            shift
            ;;
        --output-directory)
            [ "$#" -ge 2 ] || die '--output-directory requires a value.'
            output_directory=$2
            shift 2
            ;;
        --output-directory=*)
            output_directory=${1#*=}
            shift
            ;;
        --progress)
            [ "$#" -ge 2 ] || die '--progress requires a value.'
            progress=$2
            shift 2
            ;;
        --progress=*)
            progress=${1#*=}
            shift
            ;;
        --load)
            load_image=true
            shift
            ;;
        --no-cache)
            no_cache=true
            shift
            ;;
        --pull)
            pull=true
            shift
            ;;
        -h | --help)
            usage
            exit 0
            ;;
        *)
            die "Unknown option '$1'. Use --help for usage."
            ;;
    esac
done

script_root=$(script_dir)
repo_root=$(get_repo_root "$script_root")
dockerfile_path=$repo_root/docker/Dockerfile

if [ -z "$output_directory" ]; then
    output_directory=$script_root/images
fi

if [ ! -f "$dockerfile_path" ]; then
    die "Dockerfile not found: $dockerfile_path"
fi

platforms=''
if [ -z "$requested_platforms" ]; then
    requested_platforms='linux/amd64 linux/arm64'
fi

for requested_platform in $requested_platforms; do
    append_platform "$requested_platform"
done

if [ -z "$platforms" ]; then
    die 'No Docker platforms were requested.'
fi

assert_docker_cli
docker buildx version >/dev/null

resolved_output_directory=$(resolve_directory "$output_directory")
server_platform=''
archive_to_load=''

if $load_image; then
    server_platform=$(docker_server_platform)
fi

for docker_platform in $platforms; do
    tag_suffix=$(platform_tag_suffix "$docker_platform")
    archive_path=$resolved_output_directory/$(archive_name "$docker_platform" "$image_name" "$tag")
    image_tag=$image_name:$tag
    platform_image_tag=$image_name:$tag-$tag_suffix
    output=type=docker,dest=$archive_path

    rm -f "$archive_path"

    set -- \
        buildx build \
        --platform "$docker_platform" \
        --file "$dockerfile_path" \
        --tag "$image_tag" \
        --tag "$platform_image_tag" \
        --output "$output" \
        --progress "$progress"

    if $no_cache; then
        set -- "$@" --no-cache
    fi

    if $pull; then
        set -- "$@" --pull
    fi

    set -- "$@" "$repo_root"

    printf 'docker'
    for argument in "$@"; do
        printf ' %s' "$argument"
    done
    printf '\n'

    docker "$@"
    printf 'Wrote %s\n' "$archive_path"

    if [ "$docker_platform" = "$server_platform" ]; then
        archive_to_load=$archive_path
    fi
done

if $load_image; then
    if [ -z "$archive_to_load" ]; then
        die "The local Docker server platform is '$server_platform', but no matching archive was built."
    fi

    printf 'docker load --input %s\n' "$archive_to_load"
    docker load --input "$archive_to_load"
fi
