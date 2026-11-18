#!/usr/bin/env python3
"""Cisco IOS-XE install-mode software upgrade orchestrator CLI."""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any, Dict, Optional

import click
from rich.console import Console
from rich.panel import Panel
from rich.table import Table

from src.comparison import GenieSnapshotManager, GenieUnavailableError
from src.credentials import CredentialManager, setup_credentials_interactive
from src.inventory import InventoryManager, create_inventory_template
from src.orchestration import UpgradeEngine, UpgradeOperationError
from src.state import DeviceLockedError, StateConflictError, StateStore
from src.utils import create_env_template, load_config


console = Console()


def build_engine(config: Dict[str, Any]) -> UpgradeEngine:
    """Construct the runtime without opening device connections."""
    state_config = config.get("state", {})
    store = StateStore(
        state_config.get("database", "state/orchestrator.db")
    )
    credentials = CredentialManager(config)
    snapshot_manager = GenieSnapshotManager(
        state_config.get("snapshot_directory", "state/snapshots"),
        state_store=store,
    )
    jump_host_manager = None
    if config.get("connection", {}).get("use_jump_host", False):
        try:
            from src.jumphost import JumpHostManager
        except ImportError as error:
            raise click.ClickException(
                "Jump-host mode requires the sshtunnel package"
            ) from error
        jump_credentials = credentials.get_jumphost_credentials()
        if not jump_credentials["username"] or not jump_credentials["password"]:
            raise click.ClickException(
                "Jump-host mode is enabled but credentials are unavailable"
            )
        jump_host_manager = JumpHostManager(
            config.get("connection", {}).get("jump_host", {}),
            jump_credentials,
        )
    return UpgradeEngine(
        config=config,
        state_store=store,
        credential_manager=credentials,
        snapshot_manager=snapshot_manager,
        console=console,
        jump_host_manager=jump_host_manager,
    )


def resolve_job(
    engine: UpgradeEngine,
    *,
    job_id: Optional[str],
    inventory_file: Optional[str],
    mode: str,
    dry_run: bool = False,
) -> str:
    """Use an existing job or create one from an inventory file."""
    if job_id and inventory_file:
        raise click.UsageError("Use either --job-id or --file, not both")
    if job_id:
        engine.store.get_job(job_id)
        return job_id
    if not inventory_file:
        raise click.UsageError("Either --job-id or --file is required")
    return engine.create_job(
        inventory_file=inventory_file,
        mode=mode,
        dry_run=dry_run,
    )


def display_plan(engine: UpgradeEngine, job_id: str) -> None:
    """Render the persisted batch and operation plan."""
    plan = engine.get_plan(job_id)
    console.print(
        Panel.fit(
            f"[bold cyan]IOS-XE INSTALL-MODE PLAN[/bold cyan]\n"
            f"Job: {job_id}\nInventory: {plan['inventory']}",
            border_style="cyan",
        )
    )
    table = Table(show_header=True, header_style="bold magenta")
    table.add_column("Batch", justify="right")
    table.add_column("Devices")
    for number, batch in enumerate(plan["batches"], start=1):
        label = f"{number} (canary)" if number == 1 else str(number)
        table.add_row(label, ", ".join(batch))
    console.print(table)
    console.print(
        "[bold]Genie learns:[/bold] "
        + ", ".join(
            [*plan["genie_features"]["core"], *plan["genie_features"]["optional"]]
        )
    )
    console.print(
        "[bold]Install sequence:[/bold] "
        "verify -> add -> activate/reload -> compare -> commit -> soak -> cleanup"
    )


def display_summary(engine: UpgradeEngine, job_id: str) -> None:
    job = engine.store.get_job(job_id)
    summary = engine.store.summarize_job(job_id)
    console.print(engine.status_table(job_id))
    console.print(
        f"[bold]Job status:[/bold] {job['status']}  "
        f"[bold]Complete:[/bold] {summary['complete']}/{summary['total']}  "
        f"[bold]Failures:[/bold] {summary['failed']}"
    )


def require_approval(config: Dict[str, Any], message: str, approved: bool) -> None:
    if approved or not config.get("workflow", {}).get("require_approval", True):
        return
    if not click.confirm(message, default=False):
        raise click.Abort()


@click.group()
@click.version_option(version="1.0.0")
@click.option(
    "--config",
    "config_file",
    default="config/config.yaml",
    show_default=True,
    help="Configuration file path.",
)
@click.pass_context
def cli(ctx: click.Context, config_file: str) -> None:
    """Safely orchestrate parallel Catalyst 9200/9300 install-mode upgrades."""
    try:
        config = load_config(config_file)
    except (OSError, ValueError) as error:
        raise click.ClickException(str(error)) from error
    ctx.ensure_object(dict)
    ctx.obj["config"] = config
    ctx.obj["config_file"] = config_file


@cli.command("create-template")
@click.option(
    "--output",
    "-o",
    default="inventory/inventory_template.xlsx",
    show_default=True,
)
def create_template(output: str) -> None:
    """Create an Excel inventory template."""
    create_inventory_template(output)


@cli.command("create-env")
@click.option("--output", "-o", default=".env.example", show_default=True)
def create_env(output: str) -> None:
    """Create an environment-variable credential template."""
    create_env_template(output)
    console.print(f"[green]Created {output}[/green]")


@cli.command("setup-credentials")
@click.pass_context
def setup_credentials(ctx: click.Context) -> None:
    """Store credentials in the operating-system keyring."""
    try:
        setup_credentials_interactive(ctx.obj["config"])
    except (RuntimeError, ValueError, OSError) as error:
        raise click.ClickException(str(error)) from error


@cli.command("validate-inventory")
@click.option("--file", "-f", "inventory_file", required=True)
def validate_inventory(inventory_file: str) -> None:
    """Validate inventory schema and safety fields."""
    manager = InventoryManager(inventory_file)
    manager.load_inventory()
    results = manager.validate_inventory()
    for error in results["errors"]:
        console.print(f"[red]ERROR: {error}[/red]")
    for warning in results["warnings"]:
        console.print(f"[yellow]WARNING: {warning}[/yellow]")
    if not results["valid"]:
        raise click.exceptions.Exit(1)


@cli.command("plan")
@click.option("--file", "-f", "inventory_file", required=True)
@click.option("--output", "-o", type=click.Path(dir_okay=False))
@click.pass_context
def plan(ctx: click.Context, inventory_file: str, output: Optional[str]) -> None:
    """Create and display a durable approval plan."""
    engine = build_engine(ctx.obj["config"])
    try:
        job_id = engine.create_job(
            inventory_file=inventory_file,
            mode="plan",
        )
        display_plan(engine, job_id)
        if output:
            Path(output).write_text(
                json.dumps(engine.get_plan(job_id), indent=2) + "\n",
                encoding="utf-8",
            )
            console.print(f"[green]Plan written to {output}[/green]")
    except (OSError, ValueError, UpgradeOperationError) as error:
        raise click.ClickException(str(error)) from error


@cli.command("stage")
@click.option("--file", "-f", "inventory_file")
@click.option("--job-id")
@click.option("--yes", is_flag=True, help="Approve without prompting.")
@click.option("--retry-failed", is_flag=True)
@click.pass_context
def stage(
    ctx: click.Context,
    inventory_file: Optional[str],
    job_id: Optional[str],
    yes: bool,
    retry_failed: bool,
) -> None:
    """Transfer, hash-verify, and add images without reloading."""
    engine = build_engine(ctx.obj["config"])
    try:
        resolved = resolve_job(
            engine,
            job_id=job_id,
            inventory_file=inventory_file,
            mode="stage",
        )
        display_plan(engine, resolved)
        require_approval(
            ctx.obj["config"],
            "Stage images on the listed devices?",
            yes,
        )
        engine.run(
            resolved,
            mode="stage",
            retry_failed=retry_failed,
        )
        display_summary(engine, resolved)
    except (
        OSError,
        ValueError,
        UpgradeOperationError,
        DeviceLockedError,
        GenieUnavailableError,
    ) as error:
        raise click.ClickException(str(error)) from error


@cli.command("upgrade")
@click.option("--file", "-f", "inventory_file")
@click.option("--job-id")
@click.option("--dry-run", is_flag=True, help="Run read-only compatibility checks.")
@click.option("--yes", is_flag=True, help="Approve reloads without prompting.")
@click.option("--retry-failed", is_flag=True)
@click.option(
    "--retry-activation",
    is_flag=True,
    help="Explicitly allow retrying a failed/uncertain activation.",
)
@click.pass_context
def upgrade(
    ctx: click.Context,
    inventory_file: Optional[str],
    job_id: Optional[str],
    dry_run: bool,
    yes: bool,
    retry_failed: bool,
    retry_activation: bool,
) -> None:
    """Run or continue a canary/batched install-mode upgrade."""
    engine = build_engine(ctx.obj["config"])
    try:
        resolved = resolve_job(
            engine,
            job_id=job_id,
            inventory_file=inventory_file,
            mode="upgrade",
            dry_run=dry_run,
        )
        persisted_job = engine.store.get_job(resolved)
        if job_id and dry_run and not persisted_job["dry_run"]:
            raise click.UsageError(
                "--dry-run cannot be applied to an existing non-dry-run job"
            )
        effective_dry_run = bool(persisted_job["dry_run"])
        display_plan(engine, resolved)
        if not effective_dry_run:
            require_approval(
                ctx.obj["config"],
                "Activate images and reload the listed devices?",
                yes,
            )
        engine.run(
            resolved,
            mode="upgrade",
            retry_failed=retry_failed,
            retry_activation=retry_activation,
        )
        display_summary(engine, resolved)
    except (
        OSError,
        ValueError,
        UpgradeOperationError,
        DeviceLockedError,
        GenieUnavailableError,
    ) as error:
        raise click.ClickException(str(error)) from error


@cli.command("resume")
@click.option("--job-id", required=True)
@click.option("--retry-failed", is_flag=True)
@click.option("--retry-activation", is_flag=True)
@click.pass_context
def resume(
    ctx: click.Context,
    job_id: str,
    retry_failed: bool,
    retry_activation: bool,
) -> None:
    """Resume a job from its persisted per-device checkpoints."""
    engine = build_engine(ctx.obj["config"])
    try:
        engine.run(
            job_id,
            retry_failed=retry_failed,
            retry_activation=retry_activation,
        )
        display_summary(engine, job_id)
    except (
        OSError,
        ValueError,
        UpgradeOperationError,
        DeviceLockedError,
        GenieUnavailableError,
    ) as error:
        raise click.ClickException(str(error)) from error


@cli.command("status")
@click.option("--job-id")
@click.option("--events", is_flag=True, help="Show recent audit events.")
@click.pass_context
def status(ctx: click.Context, job_id: Optional[str], events: bool) -> None:
    """Show persisted per-device progress for a job."""
    engine = build_engine(ctx.obj["config"])
    if not job_id:
        latest = engine.store.latest_job()
        if not latest:
            raise click.ClickException("No jobs have been created")
        job_id = latest["job_id"]
    try:
        display_summary(engine, job_id)
        if events:
            event_table = Table(title="Recent audit events")
            event_table.add_column("Time")
            event_table.add_column("Device")
            event_table.add_column("Level")
            event_table.add_column("Message")
            for event in reversed(engine.store.list_events(job_id, limit=50)):
                event_table.add_row(
                    event["created_at"][11:19],
                    event["hostname"] or "-",
                    event["level"],
                    event["message"],
                )
            console.print(event_table)
    except KeyError as error:
        raise click.ClickException(str(error)) from error


@cli.command("cleanup")
@click.option("--job-id", required=True)
@click.option("--device", "devices", multiple=True)
@click.option(
    "--wait-for-soak",
    is_flag=True,
    help="Wait until all selected devices reach cleanup eligibility.",
)
@click.option("--yes", is_flag=True)
@click.pass_context
def cleanup(
    ctx: click.Context,
    job_id: str,
    devices: tuple[str, ...],
    wait_for_soak: bool,
    yes: bool,
) -> None:
    """Run `install remove inactive` after health gates and soak time."""
    require_approval(
        ctx.obj["config"],
        "Permanently remove inactive IOS-XE packages?",
        yes,
    )
    engine = build_engine(ctx.obj["config"])
    try:
        engine.cleanup(
            job_id,
            hostnames=list(devices) or None,
            wait_for_soak=wait_for_soak,
        )
        display_summary(engine, job_id)
    except (
        OSError,
        ValueError,
        KeyError,
        UpgradeOperationError,
        DeviceLockedError,
    ) as error:
        raise click.ClickException(str(error)) from error


@cli.command("rollback")
@click.option("--job-id", required=True)
@click.option("--device", required=True)
@click.option("--yes", is_flag=True)
@click.pass_context
def rollback(
    ctx: click.Context,
    job_id: str,
    device: str,
    yes: bool,
) -> None:
    """Abort an activated, uncommitted image and reload the prior version."""
    require_approval(
        ctx.obj["config"],
        f"Abort the uncommitted install and reload {device}?",
        yes,
    )
    engine = build_engine(ctx.obj["config"])
    try:
        engine.rollback(job_id, device)
        display_summary(engine, job_id)
    except (
        OSError,
        ValueError,
        KeyError,
        UpgradeOperationError,
        DeviceLockedError,
    ) as error:
        raise click.ClickException(str(error)) from error


@cli.command("report")
@click.option("--file", "-f", "inventory_file", required=True)
@click.option("--output", "-o")
def report(inventory_file: str, output: Optional[str]) -> None:
    """Export an Excel inventory status summary."""
    manager = InventoryManager(inventory_file)
    manager.load_inventory()
    manager.export_summary_report(output)


@cli.command("jobs")
@click.option("--limit", default=20, show_default=True)
@click.pass_context
def jobs(ctx: click.Context, limit: int) -> None:
    """List recent jobs and their status."""
    engine = build_engine(ctx.obj["config"])
    records = engine.store.list_jobs(limit=limit)
    if not records:
        console.print("No jobs have been created")
        return
    table = Table(title="Upgrade jobs")
    table.add_column("Job")
    table.add_column("Mode")
    table.add_column("Status")
    table.add_column("Operator")
    table.add_column("Created")
    for record in records:
        table.add_row(
            record["job_id"],
            record["mode"],
            record["status"],
            record["operator"] or "-",
            (record["created_at"] or "")[:19].replace("T", " "),
        )
    console.print(table)


@cli.command("cancel")
@click.option("--job-id", required=True)
@click.option(
    "--force",
    is_flag=True,
    help="Cancel even when devices may be mid-upgrade (verify by hand first).",
)
@click.option("--reason", default="", help="Optional note stored in the audit log.")
@click.option("--yes", is_flag=True)
@click.pass_context
def cancel(
    ctx: click.Context,
    job_id: str,
    force: bool,
    reason: str,
    yes: bool,
) -> None:
    """Cancel a failed or stalled job and free its device reservations."""
    require_approval(
        ctx.obj["config"],
        f"Cancel job {job_id} and release its device reservations?",
        yes,
    )
    engine = build_engine(ctx.obj["config"])
    try:
        summary = engine.cancel_job(job_id, force=force, reason=reason)
        display_summary(engine, job_id)
        console.print(
            f"[green]Cancelled job {job_id}; released "
            f"{summary.get('released', 0)} device reservation(s)[/green]"
        )
    except StateConflictError as error:
        raise click.ClickException(
            f"{error} Re-run with --force once you have confirmed device state."
        ) from error
    except (
        OSError,
        ValueError,
        KeyError,
        UpgradeOperationError,
        DeviceLockedError,
    ) as error:
        raise click.ClickException(str(error)) from error


# Kept as an alias for existing users.
cli.add_command(validate_inventory, "validate")


if __name__ == "__main__":
    cli()
