B_STATE=$(cd "$(dirname "$0")" && pwd)
export DEBIAN_FRONTEND=noninteractive

b_rm() {
    rm -f -- "$1"
}

b_rm_tree() {
    if [ -L "$1" ]; then
        rm -f -- "$1"
    elif [ -d "$1" ]; then
        rm -rf -- "$1"
    fi
}

b_rm_path() {
    b_rm_tree "$1"
    rm -f -- "$1"
}

b_rm_empty() {
    if [ -d "$1" ] && [ ! -L "$1" ]; then
        rmdir -- "$1" 2>/dev/null
    fi
    return 0
}

b_rm_empty_parents() {
    local parent
    parent=$(dirname -- "$1")
    while [ "$parent" != "$2" ] && [ "$parent" != "/" ] && [ "$parent" != "." ]; do
        case "$parent" in
            "$2"*) ;;
            *) break ;;
        esac
        rmdir -- "$parent" 2>/dev/null || break
        parent=$(dirname -- "$parent")
    done
    return 0
}

b_rm_glob() {
    local folder
    for folder in "$1"/$2; do
        if [ -d "$folder" ] && [ ! -L "$folder" ]; then
            rm -rf -- "$folder"
        fi
    done
    return 0
}

b_rm_link() {
    local target
    target=$(readlink -- "$1") || return 0
    if [ $# -lt 2 ] || [ "$target" = "$2" ]; then
        rm -f -- "$1"
        return 0
    fi
    case "$target" in
        "$2"/*) rm -f -- "$1" ;;
    esac
    return 0
}

b_restore_backup() {
    if [ -f "$1" ] && grep -qF -- "$2" "$1"; then
        rm -f -- "$1"
        if [ -f "$1.bundler.bak" ]; then
            mv -- "$1.bundler.bak" "$1"
        fi
    fi
    return 0
}

b_systemd_running() {
    [ -d /run/systemd/system ]
}

b_systemctl() {
    if b_systemd_running; then
        systemctl $1 "$2" >/dev/null 2>&1
    fi
    return 0
}

b_daemon_reload() {
    if b_systemd_running; then
        systemctl daemon-reload >/dev/null 2>&1
    fi
    return 0
}

b_run() {
    "$@" >/dev/null 2>&1
    return 0
}

b_run_as() {
    local user=$1 home
    shift
    home=$(getent passwd "$user" | cut -d: -f6)
    echo "as $user: $*"
    (cd / && runuser -u "$user" -- env "HOME=$home" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 "$@" >/dev/null 2>&1)
    return 0
}

b_info() {
    echo "$1"
}

b_warn() {
    echo "warning: $1"
}

b_has_files() {
    local folder
    for folder in "$@"; do
        if [ -d "$folder" ] && [ -n "$(find "$folder" -type f -print -quit 2>/dev/null)" ]; then
            return 0
        fi
    done
    return 1
}

b_block_remove() {
    local file=$1 tag=$2 temp
    [ -f "$file" ] && grep -qxF -- "# bundler $tag begin" "$file" || return 0
    temp=$(mktemp) || return 0
    awk -v begin="# bundler $tag begin" -v end="# bundler $tag end" '
        inside { if ($0 == end) { inside = 0 } next }
        $0 == begin { inside = 1; next }
        { print }
    ' "$file" > "$temp" && cat "$temp" > "$file"
    rm -f -- "$temp"
    return 0
}

b_git_unset() {
    command -v git >/dev/null 2>&1 || return 0
    if [ "$(git config --system --get "$1" 2>/dev/null)" = "$2" ]; then
        git config --system --unset "$1" >/dev/null 2>&1
    fi
    return 0
}

b_disable_linger() {
    if ! loginctl disable-linger "$1" >/dev/null 2>&1; then
        rm -f -- "/var/lib/systemd/linger/$1"
    fi
    return 0
}

b_json_unset() {
    local user=$1 file=$2 changed
    [ -f "$file" ] || return 0
    changed=$(python3 - "$file" "$3" <<'PYTHON'
import json, os, shutil, sys

path, expected = sys.argv[1], json.loads(sys.argv[2])

def lenient(text):
    out = []
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        if c == '"':
            j = i + 1
            while j < n and text[j] != '"':
                j += 2 if text[j] == '\\' else 1
            out.append(text[i:j + 1])
            i = j + 1
        elif text.startswith('//', i):
            while i < n and text[i] != '\n':
                i += 1
        elif text.startswith('/*', i):
            end = text.find('*/', i + 2)
            i = n if end < 0 else end + 2
        elif c == ',':
            j = i + 1
            while j < n:
                if text[j].isspace():
                    j += 1
                elif text.startswith('//', j):
                    while j < n and text[j] != '\n':
                        j += 1
                elif text.startswith('/*', j):
                    end = text.find('*/', j + 2)
                    j = n if end < 0 else end + 2
                else:
                    break
            if j < n and text[j] in '}]':
                i += 1
            else:
                out.append(c)
                i += 1
        else:
            out.append(c)
            i += 1
    return ''.join(out)

try:
    raw = open(path, encoding='utf-8').read()
    data = json.loads(lenient(raw)) if raw.strip() else {}
    if not isinstance(data, dict):
        raise ValueError('not a JSON object')
except Exception as error:
    print('could not parse %s (%s); not changed' % (path, error), file=sys.stderr)
    sys.exit(0)

changed = False
for key, value in expected.items():
    if key in data and data[key] == value:
        del data[key]
        changed = True

if not changed:
    sys.exit(0)

if not data:
    os.remove(path)
else:
    try:
        json.loads(raw)
    except Exception:
        if not os.path.exists(path + '.bundler.bak'):
            shutil.copy(path, path + '.bundler.bak')
    with open(path, 'w', encoding='utf-8') as handle:
        handle.write(json.dumps(data, indent=2, ensure_ascii=False) + '\n')
print('changed')
PYTHON
    ) || return 0
    if [ "$changed" = changed ]; then
        chown -h "$user:" -- "$file" 2>/dev/null
    fi
    return 0
}

b_model_remove() {
    local models=$1 manifest=$2 digest
    [ -f "$manifest" ] || return 0
    local digests
    digests=$(grep -o 'sha256:[0-9a-f]\{64\}' "$manifest" | sed 's/^sha256://' | sort -u)
    local relative=${manifest#"$models"/manifests/}
    rm -f -- "$manifest" "$models/manifests-v2/$relative"
    for digest in $digests; do
        if ! grep -rqF -- "sha256:$digest" "$models/manifests" "$models/manifests-v2" 2>/dev/null; then
            rm -f -- "$models/blobs/sha256-$digest" "$models/metadata/sha256-$digest.json"
        fi
    done
    return 0
}

b_feed_remove() {
    local feed=$1 own=$B_STATE relative other target parent
    [ -f "$own/nuget-added" ] || return 0
    while IFS= read -r relative; do
        [ -n "$relative" ] || continue
        local handed=0
        for other in $(printf '%s\n' "$(dirname "$own")"/*/ | sort); do
            other=${other%/}
            [ "$other" != "$own" ] || continue
            if [ -f "$other/nuget-shared" ] && grep -qxF -- "$relative" "$other/nuget-shared"; then
                echo "$relative" >> "$other/nuget-added"
                grep -vxF -- "$relative" "$other/nuget-shared" > "$other/nuget-shared.tmp"
                mv -- "$other/nuget-shared.tmp" "$other/nuget-shared"
                handed=1
                break
            fi
        done
        [ "$handed" = 1 ] && continue
        target="$feed/$relative"
        [ -d "$target" ] && rm -rf -- "$target"
        parent=$(dirname -- "$target")
        [ -d "$parent" ] && rmdir -- "$parent" 2>/dev/null
    done < "$own/nuget-added"
    return 0
}

b_installed_packages() {
    local package status
    for package in "$@"; do
        status=$(dpkg-query -W -f '${db:Status-Status}\n' "$package" 2>/dev/null | head -n 1)
        [ "$status" = installed ] && echo "$package"
    done
    return 0
}

b_removed_by() {
    sed -n 's/^Remv \([^ ]*\).*/\1/p'
}

b_apt_removable_alone() {
    local running=$1 package output name own=" " plain
    shift
    for package in "$@"; do
        own="$own${package%%:*} "
    done
    for package in "$@"; do
        case "$package" in *"$running"*) continue ;; esac
        output=$(apt-get -s remove "$package" 2>&1) || continue
        local alone=1
        for name in $(printf '%s\n' "$output" | b_removed_by); do
            plain=${name%%:*}
            case "$own" in *" $plain "*) ;; *) alone=0; break ;; esac
        done
        [ "$alone" = 1 ] && echo "$package"
    done | LC_ALL=C sort -u
}

b_apt_remove_added() {
    local own=$B_STATE leftover running pass package removable simulation remove
    local -a candidates present
    leftover="$(dirname "$own")/apt-leftover"
    running=$(cat /proc/sys/kernel/osrelease)
    mapfile -t candidates < <({ cat "$own/apt-new" "$leftover" 2>/dev/null; } | awk 'NF && !seen[$0]++')
    for pass in 1 2 3 4; do
        mapfile -t present < <(b_installed_packages "${candidates[@]}")
        [ "${#present[@]}" -gt 0 ] || break
        apt-mark auto "${present[@]}" >/dev/null 2>&1
        simulation=$(apt-get -s -o APT::AutoRemove::RecommendsImportant=false -o APT::AutoRemove::SuggestsImportant=false autoremove 2>&1)
        removable=$(printf '%s\n' "$simulation" | b_removed_by)
        remove=$(
            for package in "${present[@]}"; do
                case "$package" in *"$running"*) continue ;; esac
                if printf '%s\n' "$removable" | grep -qxF -- "$package" || [[ $package =~ ^linux-.*-[0-9]+\.[0-9]+\.[0-9]+-[0-9]+-[a-z0-9]+$ ]]; then
                    echo "$package"
                fi
            done | LC_ALL=C sort -u
        )
        if [ -z "$remove" ]; then
            remove=$(b_apt_removable_alone "$running" "${present[@]}")
        fi
        [ -n "$remove" ] || break
        mapfile -t remove < <(printf '%s\n' "$remove")
        apt-get remove -y --purge "${remove[@]}"
    done
    local -a remaining
    mapfile -t remaining < <(b_installed_packages "${candidates[@]}")
    if [ "${#remaining[@]}" -eq 0 ]; then
        rm -f -- "$leftover"
    else
        printf '%s\n' "${remaining[@]}" > "$leftover"
    fi
    return 0
}
