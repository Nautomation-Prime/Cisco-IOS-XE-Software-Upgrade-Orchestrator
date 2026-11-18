# Cisco IOS-XE Software Upgrade Orchestrator

A resumable Python CLI for upgrading multiple Cisco Catalyst switches with the
IOS-XE install workflow. It uses bounded parallel batches, persistent per-device
state, and pyATS/Genie before-and-after comparisons to reduce the risk of a
failed or partially completed maintenance window.

> This tool performs disruptive network changes. Test it in a lab, verify
> console access, review Cisco release notes, and use an approved maintenance
> window before production use.

## Supported scope

- Catalyst 9200 and 9300 families
- Standalone switches by default
- Switch stacks only when explicitly enabled with `safety.allow_stacks: true`
- IOS-XE install mode only (`flash:packages.conf`)
- Local or pre-staged images with MD5 verification
- Direct SSH or one configured jump host
- Linux or WSL runtime for pyATS/Genie

Bundle mode, ISSU, and dual-supervisor workflows are deliberately blocked.
Stacks remain a guarded opt-in path and should be lab-validated before use in
production.

## Safety model

- SQLite is the execution source of truth; Excel is only an inventory and status
  mirror.
- Each disruptive intent is persisted before its command is sent.
- A process restart during reload resumes at reconnect and post-checks; it does
  not replay `install activate`.
- A target image must pass local and device MD5 checks before activation.
- Canary and bounded batches stop after configured failures.
- Members with the same `redundancy_group` are never placed in one batch.
- `install activate` and `install commit` are separate. The image is committed
  only after exact version checks and required Genie comparisons pass.
- `install activate` sets an explicit auto-abort timer
  (`upgrade.auto_abort_timer_minutes`, IOS-XE accepts 30-1200). The resulting
  commit deadline is persisted, shown in the device message, and re-checked
  before `install commit` runs, so a job resumed after the timer expired
  reports what actually happened instead of committing blind.
- `install remove inactive` runs only after commit and a configurable soak
  period.
- Address-based reservations and renewable execution leases prevent overlapping
  jobs or processes.

## Workflow

1. Validate inventory and create an approval plan.
2. Discover platform, install mode, stack state, boot target, install state, and
   flash capacity.
3. Validate image family, target version, upgrade direction, local MD5, and
   optional console confirmation.
4. Back up running/startup configuration and device facts.
5. Learn the pre-upgrade Genie baseline.
6. Copy and verify the image, then run `install add`.
7. Activate canary/batches with `install activate`.
8. Persist `rebooting`, reconnect after SSH returns, and reconcile observed
   version/install state.
9. Wait for control-plane stabilization, learn the post-upgrade baseline, and
   compare it with the original. Only the learned Ops state (`Ops.info`) is
   persisted and compared, with excluded keys pruned from both snapshots
   before the diff, so counters and timers cannot fail the gate.
10. Run `install commit` only for healthy devices.
11. Retain inactive files for the soak period.
12. Run `install remove inactive` and mark the device complete.

## Genie comparisons

The orchestrator attempts these core Ops learns:

- `interface`
- `platform`
- `vlan`
- `stp`
- `lag`
- `lldp`

It also attempts protocol-specific learns:

- `routing`
- `ospf`
- `bgp`
- `arp`

`interface` and `platform` are required by default. Other features are compared
when they were successfully captured before the upgrade. Volatile counters and
timers are excluded, while interface state, VLAN membership, spanning-tree
state, EtherChannel membership, neighbors, adjacencies, and forwarding
structure remain visible. Version and image are verified separately because
they are expected to change.

Tune `required_features` and `critical_features` in
`config/config.yaml` for the protocols deployed in your network.

## Installation

pyATS/Genie is supported on Linux and macOS, not native Windows. On Windows, run
the upgrade workflow inside WSL.

```bash
python3 -m venv .venv
source .venv/bin/activate
python -m pip install --upgrade pip
pip install -r requirements-genie.txt
cp config/config.example.yaml config/config.yaml
cp .env.example .env
```

Set credentials through the system keyring:

```bash
python orchestrator.py setup-credentials
```

Or provide `DEVICE_USERNAME`, `DEVICE_PASSWORD`, and optionally
`DEVICE_ENABLE_PASSWORD` through the environment or `.env`.

## Quick start

```bash
# Create and edit inventory/devices.xlsx
python orchestrator.py create-template --output inventory/devices.xlsx

# Validate and review the exact batches
python orchestrator.py validate-inventory --file inventory/devices.xlsx
python orchestrator.py plan --file inventory/devices.xlsx

# Optional: transfer, verify, and add packages before the maintenance window
python orchestrator.py stage --file inventory/devices.xlsx

# Start a new upgrade, or activate a previously staged plan by job ID
python orchestrator.py upgrade --file inventory/devices.xlsx
python orchestrator.py upgrade --job-id <job-id>

# Inspect or resume durable state
python orchestrator.py status --job-id <job-id> --events
python orchestrator.py resume --job-id <job-id>

# After the configured soak period
python orchestrator.py cleanup --job-id <job-id>
python orchestrator.py cleanup --job-id <job-id> --wait-for-soak
```

`resume` is the normal recovery command after the CLI process or host restarts.
Devices persisted as `rebooting`, `reconnecting`, `post_checking`,
`commit_pending`, or `cleanup_pending` continue from that stage.

## Inventory

| Column | Required | Purpose |
|---|---:|---|
| `hostname` | Yes | Unique device name |
| `ip_address` | Yes | SSH management address |
| `platform` | Yes | `catalyst9200` or `catalyst9300` |
| `target_version` | Yes | Exact target, such as `17.12.4` |
| `image_path` | Yes | Local Linux/WSL path or `flash:<image>` |
| `image_md5` | Yes | 32-character expected MD5 |
| `ssh_port` | No | SSH port, defaults to 22 |
| `redundancy_group` | No | Devices that must be placed in separate batches |
| `console_verified` | Policy | `Yes` when out-of-band access is confirmed |
| `maintenance_window_start` | No | ISO-8601 activation-window start |
| `maintenance_window_end` | No | ISO-8601 activation-window end |
| `enabled` | No | Set `No` to omit a row |
| `status` | No | Latest mirrored durable state |
| `notes` | No | Failure/warning details |

Use timezone-aware maintenance values, for example
`2026-08-01T22:00:00+01:00`.

## Commands

| Command | Purpose |
|---|---|
| `create-template` | Create a starter Excel inventory workbook |
| `create-env` | Create an example `.env` credential file |
| `setup-credentials` | Store device and jump-host credentials in the system keyring |
| `validate-inventory` | Validate schema and required safety data |
| `validate` | Alias for `validate-inventory` |
| `plan` | Persist and display canary/batch/command intent |
| `stage` | Transfer, hash-check, and run `install add` without reload |
| `upgrade` | Run or continue activation and verification |
| `resume` | Resume all nonterminal device states |
| `status` | Display per-device state and audit events |
| `jobs` | List recent jobs and their status |
| `cleanup` | Remove inactive packages after the soak gate, optionally waiting for soak eligibility |
| `rollback` | Abort an activated, uncommitted target image |
| `cancel` | Cancel a failed or stalled job and release its device reservations |
| `report` | Export an Excel status summary |

Activation retries are never automatic after an uncertain result. They require
both `--retry-failed` and `--retry-activation`.

## Device prerequisites

Because staging copies the image to the switch over SCP, each target device must
have the SCP server enabled and be reachable with a privilege-15 account:

```text
ip scp server enable
```

If this is missing, staging fails with `Administratively prohibited` (the SSH
server refusing the SCP channel). The orchestrator now reports this with the
exact remediation instead of the raw paramiko error.

## Recovering from a failed job

A job holds a reservation on every device it targets so two jobs can never touch
the same switch at once. If a job fails early (for example, a missing image file
leaves devices in `staging_failed`), that reservation stays in place and later
jobs for the same devices are refused with a "locked by job" error.

Use `cancel` to release a failed or stalled job:

```bash
python orchestrator.py jobs                 # find the stuck job id
python orchestrator.py cancel --job-id <job-id>
```

`cancel` marks the job's unfinished devices as `failed` and frees their
reservations. It refuses while another process is still running the job, and by
default it refuses when a device has moved past staging (where the switch may be
mid-upgrade). After confirming such a device by hand, override with `--force`.

## Durable state and artifacts

- `state/orchestrator.db`: jobs, locks, transitions, and audit events
- `state/snapshots/<job>/<device>/`: Genie before/after JSON and diff reports
- `backups/<device>/`: configuration and pre-change command output
- Excel `status`, `last_updated`, and `notes`: operator-friendly mirror

Do not delete the state directory while a job is active. Credentials are
redacted before job configuration is persisted.

## Rollback limits

Automatic rollback is only safe while the new image is activated and
uncommitted. The `rollback` command uses `install abort`, reloads, and verifies
the recorded previous version. Once the target has been committed, recovery
requires an explicitly planned software change; the tool does not claim that
`install rollback to committed` can restore an older already-replaced committed
baseline.

## Development

```bash
python -m unittest discover -s tests -v
```

Device-facing behavior must also be validated against the exact Catalyst model
and IOS-XE release in a lab.

## Desktop GUI Application (Avalonia UI 11 / FluentAvalonia)

A cross-platform Fluent desktop interface (Windows 11 & macOS aware) is provided alongside the original Python CLI with zero breaking changes to CLI workflows:

- **Technology**: Avalonia UI 11.2 + FluentAvalonia 2.1 (`.NET 8`) bridged to the Python automation engine via `gui_bridge.py` and streaming subprocess execution.
- **4-Stage Guided Upgrade Pipeline**:
  1. **Inventory**: Card-based Switch Inventory Deck & Card Editor with real-time field validation, duplicate hostname/IP detection, `.bin` firmware picker with automatic IOS-XE version inference, non-blocking MD5 checksum calculation with progress bar, and optional Excel/CSV import/export (no Excel required).
  2. **Plan & Batches**: Automatically syncs Stage 1 switches and generates canary/wave upgrade plans with dry-run support and Job ID propagation.
  3. **Execute**: Live streaming console output (batched for smooth rendering), interactive stage locking during active runs, and always-visible validated SSH credentials (username, password, optional enable secret) injected securely in-memory per run (no `.env` file required).
  4. **History**: SQLite `StateStore` job history viewer with live status filter tiles (`All`, `Active`, `Halted`, `Failed`, `Completed`, `Cancelled`), per-device telemetry inspection, safe individual job deletion, and 2-step **Clear history** (automatically protecting actively leased/running jobs).
- **Encrypted SSH Credentials Vault**: Local AES-256-GCM + PBKDF2-SHA256 (600,000 iterations) vault for saving reusable SSH credential presets and loading them directly into Stage 3.
- **Native Desktop Polish**: 3-way synchronized theming (Light, Dark, System Default), native Windows caption glyphs & macOS traffic-light/menu integration, keyboard shortcuts (`Ctrl`/`⌘` `1`–`4`, `,`, `E`, `K`, `N`, `+`/`-`/`0`), and high-DPI UI zoom scaling (80%–150%).

### Running the Desktop GUI

```cmd
run_gui.bat
```
or via PowerShell:
```powershell
.\run_gui.ps1
```

### Building a Complete Standalone Release (Windows)

```powershell
.\publish-all.ps1 -Version 1.0.0
```
Packages a self-contained `.NET 8` executable, relocatable embedded Python 3.12 runtime with all dependencies, Inno Setup installer, portable ZIP, `SHA256SUMS.txt`, and `update_manifest.json` under `dist/`.

### Running GUI Headless Tests

```powershell
dotnet test gui\CiscoUpgradeOrchestrator.Gui.Tests\CiscoUpgradeOrchestrator.Gui.Tests.csproj
```

## License

Apache License 2.0 — see [LICENSE](LICENSE) and [NOTICE](NOTICE). Free to use, modify and redistribute. If you'd like it tailored, deployed or extended for your estate, see [Work With Me](https://www.nautomationprime.io/work-with-me/).
