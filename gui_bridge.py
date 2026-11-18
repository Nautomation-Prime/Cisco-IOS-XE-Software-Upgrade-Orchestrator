"""GUI Bridge for Cisco IOS-XE Software Upgrade Orchestrator.

Provides fast, structured JSON interfaces for the Avalonia Fluent GUI application.
"""

from __future__ import annotations

import argparse
import contextlib
import importlib.util
import io
import json
import os
import sys
from pathlib import Path
from typing import Any, Dict, List

# Ensure repository root is on sys.path
REPO_ROOT = Path(__file__).resolve().parent
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))


def _resolve_repo_path(path_value: str) -> Path:
    path = Path(path_value)
    if path.is_absolute():
        return path
    return (REPO_ROOT / path).resolve()


def _resolve_config_path(config_path: str = "config/config.yaml") -> str:
    candidate = _resolve_repo_path(config_path)
    if candidate.exists():
        return str(candidate)
    example = REPO_ROOT / "config" / "config.example.yaml"
    if example.exists():
        return str(example)
    return str(candidate)


def _resolve_db_path(config: Dict[str, Any]) -> str:
    # Matches orchestrator.py default: state/orchestrator.db
    raw = config.get("state", {}).get("database", "state/orchestrator.db")
    return str(_resolve_repo_path(str(raw)))


def test_environment() -> Dict[str, Any]:
    """Fast non-importing check of Python interpreter and required packages."""
    deps = {
        "nornir": False,
        "netmiko": False,
        "openpyxl": False,
        "rich": False,
        "click": False,
        "paramiko": False,
        "scp": False,
        "keyring": False,
        "yaml": False,
    }
    for mod in list(deps.keys()):
        try:
            deps[mod] = importlib.util.find_spec(mod) is not None
        except Exception:
            deps[mod] = False

    state_store_available = (REPO_ROOT / "src" / "state" / "__init__.py").exists()
    inventory_available = (REPO_ROOT / "src" / "inventory" / "__init__.py").exists() and deps["openpyxl"]
    import_error = None

    if not all(deps.values()):
        missing = [name for name, ok in deps.items() if not ok]
        import_error = f"Missing packages: {', '.join(missing)}"

    return {
        "python_executable": sys.executable,
        "python_version": sys.version,
        "cwd": os.getcwd(),
        "dependencies": deps,
        "all_installed": all(deps.values()),
        "state_store_available": state_store_available,
        "inventory_manager_available": inventory_available,
        "import_error": import_error,
    }


def get_jobs(config_path: str = "config/config.yaml", limit: int = 100) -> List[Dict[str, Any]]:
    """Return historical jobs from StateStore as JSON."""
    try:
        from src.state import StateStore
        from src.utils import load_config

        resolved = _resolve_config_path(config_path)
        config = load_config(resolved)
        db_path = _resolve_db_path(config)
        store = StateStore(db_path)
        return store.list_jobs(limit=limit)
    except Exception as exc:
        return [{"error": str(exc)}]


def get_job_devices(job_id: str, config_path: str = "config/config.yaml") -> Dict[str, Any]:
    """Return devices and their state summary for a given job."""
    try:
        from src.state import StateStore
        from src.utils import load_config

        resolved = _resolve_config_path(config_path)
        config = load_config(resolved)
        db_path = _resolve_db_path(config)
        store = StateStore(db_path)
        devices = store.get_devices(job_id)
        summary = store.summarize_job(job_id)
        events = store.list_events(job_id, limit=50)
        return {
            "job_id": job_id,
            "devices": devices,
            "summary": summary,
            "events": events or [],
        }
    except Exception as exc:
        return {"error": str(exc), "devices": [], "summary": {}}


def _job_has_active_lease(connection: Any, job_id: str, now_iso: str) -> bool:
    """Return True if a job currently holds a non-expired execution lease."""
    job_lock = connection.execute(
        "SELECT 1 FROM job_execution_locks WHERE job_id = ? AND expires_at > ? LIMIT 1",
        (job_id, now_iso),
    ).fetchone()
    if job_lock is not None:
        return True
    dev_lock = connection.execute(
        "SELECT 1 FROM execution_locks WHERE job_id = ? AND expires_at > ? LIMIT 1",
        (job_id, now_iso),
    ).fetchone()
    return dev_lock is not None


def delete_job(job_id: str, config_path: str = "config/config.yaml") -> Dict[str, Any]:
    """Safely delete a single inactive job and all cascaded records from StateStore."""
    try:
        import sqlite3
        from datetime import datetime, timezone
        from src.state import StateStore
        from src.utils import load_config

        resolved = _resolve_config_path(config_path)
        config = load_config(resolved)
        db_path = _resolve_db_path(config)
        # Ensure schema exists
        StateStore(db_path)

        now_iso = datetime.now(timezone.utc).isoformat()
        conn = sqlite3.connect(db_path, timeout=30)
        try:
            conn.execute("PRAGMA foreign_keys = ON")
            conn.execute("PRAGMA busy_timeout = 30000")
            row = conn.execute("SELECT status FROM jobs WHERE job_id = ?", (job_id,)).fetchone()
            if row is None:
                return {"success": False, "deleted": 0, "error": f"Job '{job_id}' not found."}

            if _job_has_active_lease(conn, job_id, now_iso):
                return {
                    "success": False,
                    "deleted": 0,
                    "error": f"Job '{job_id}' has an active execution lease. Stop or release its lease before deleting.",
                }

            conn.execute("DELETE FROM jobs WHERE job_id = ?", (job_id,))
            conn.commit()
        finally:
            conn.close()

        return {
            "success": True,
            "deleted": 1,
            "skipped_active": 0,
            "message": f"Deleted job {job_id} and its associated history records.",
        }
    except Exception as exc:
        return {"success": False, "deleted": 0, "error": str(exc)}


def clear_history(config_path: str = "config/config.yaml") -> Dict[str, Any]:
    """Safely delete all inactive jobs from StateStore while protecting actively leased runs."""
    try:
        import sqlite3
        from datetime import datetime, timezone
        from src.state import StateStore
        from src.utils import load_config

        resolved = _resolve_config_path(config_path)
        config = load_config(resolved)
        db_path = _resolve_db_path(config)
        StateStore(db_path)

        now_iso = datetime.now(timezone.utc).isoformat()
        deleted = 0
        skipped_active = 0

        conn = sqlite3.connect(db_path, timeout=30)
        try:
            conn.execute("PRAGMA foreign_keys = ON")
            conn.execute("PRAGMA busy_timeout = 30000")
            job_ids = [r[0] for r in conn.execute("SELECT job_id FROM jobs").fetchall()]

            for jid in job_ids:
                if _job_has_active_lease(conn, jid, now_iso):
                    skipped_active += 1
                    continue
                conn.execute("DELETE FROM jobs WHERE job_id = ?", (jid,))
                deleted += 1

            conn.commit()
        finally:
            conn.close()

        msg = f"Cleared {deleted} job(s) from StateStore."
        if skipped_active > 0:
            msg += f" Skipped {skipped_active} active leased job(s)."

        return {
            "success": True,
            "deleted": deleted,
            "skipped_active": skipped_active,
            "message": msg,
        }
    except Exception as exc:
        return {"success": False, "deleted": 0, "skipped_active": 0, "error": str(exc)}


def parse_inventory(file_path: str) -> Dict[str, Any]:
    """Parse an inventory Excel file and return rows and validation results."""
    try:
        from src.inventory import InventoryManager

        sink = io.StringIO()
        with contextlib.redirect_stdout(sink):
            manager = InventoryManager(file_path)
            devices = manager.load_inventory()
            validation = manager.validate_inventory()
        return {
            "valid": validation.get("valid", True),
            "errors": validation.get("errors", []),
            "warnings": validation.get("warnings", []),
            "total_devices": len(devices),
            "devices": devices,
        }
    except Exception as exc:
        return {
            "valid": False,
            "errors": [str(exc)],
            "warnings": [],
            "total_devices": 0,
            "devices": [],
        }


def save_inventory(file_path: str, devices_json_path: str) -> Dict[str, Any]:
    """Write GUI-entered device cards to an Excel workbook and validate via InventoryManager."""
    try:
        from openpyxl import Workbook
        from openpyxl.styles import Alignment, Font, PatternFill
        from src.inventory import InventoryManager

        with open(devices_json_path, "r", encoding="utf-8") as handle:
            devices = json.load(handle)

        if not isinstance(devices, list):
            raise ValueError("Device payload must be a JSON array")

        output_path = Path(file_path)
        output_path.parent.mkdir(parents=True, exist_ok=True)

        wb = Workbook()
        ws = wb.active
        ws.title = "Devices"

        headers = [
            "hostname",
            "ip_address",
            "platform",
            "current_version",
            "target_version",
            "image_path",
            "image_md5",
            "maintenance_window",
            "maintenance_window_start",
            "maintenance_window_end",
            "redundancy_group",
            "console_verified",
            "enabled",
            "ssh_port",
            "status",
            "last_updated",
            "notes",
            "backup_path",
            "rollback_available",
        ]

        for col_idx, header in enumerate(headers, start=1):
            cell = ws.cell(1, col_idx, header)
            cell.font = Font(bold=True, color="FFFFFF")
            cell.fill = PatternFill(start_color="4472C4", end_color="4472C4", fill_type="solid")
            cell.alignment = Alignment(horizontal="center")

        for row_idx, device in enumerate(devices, start=2):
            for col_idx, header in enumerate(headers, start=1):
                value = device.get(header, "")
                if value is None:
                    value = ""
                ws.cell(row_idx, col_idx, value)

        wb.save(output_path)

        sink = io.StringIO()
        with contextlib.redirect_stdout(sink):
            manager = InventoryManager(str(output_path))
            loaded = manager.load_inventory()
            validation = manager.validate_inventory()

        return {
            "success": True,
            "valid": validation.get("valid", True),
            "errors": validation.get("errors", []),
            "warnings": validation.get("warnings", []),
            "total_devices": len(loaded),
            "devices": loaded,
            "file_path": str(output_path),
        }
    except Exception as exc:
        return {
            "success": False,
            "valid": False,
            "errors": [str(exc)],
            "warnings": [],
            "total_devices": 0,
            "file_path": file_path,
        }


def get_config_json(config_path: str = "config/config.yaml") -> Dict[str, Any]:
    """Return current configuration as JSON."""
    try:
        from src.utils import load_config

        resolved = _resolve_config_path(config_path)
        return load_config(resolved)
    except Exception as exc:
        return {"error": str(exc)}


def create_plan(inventory_file: str, config_path: str = "config/config.yaml") -> Dict[str, Any]:
    """Create and return upgrade plan for an inventory file."""
    try:
        from src.utils import load_config
        from orchestrator import build_engine

        sink = io.StringIO()
        with contextlib.redirect_stdout(sink):
            resolved = _resolve_config_path(config_path)
            config = load_config(resolved)
            engine = build_engine(config)
            job_id = engine.create_job(inventory_file=inventory_file, mode="plan")
            plan_data = engine.get_plan(job_id)
        return {"success": True, "plan": plan_data}
    except Exception as exc:
        return {"success": False, "error": str(exc)}


def main() -> None:
    parser = argparse.ArgumentParser(description="GUI Bridge")
    subparsers = parser.add_subparsers(dest="command")

    subparsers.add_parser("test-env")

    jobs_parser = subparsers.add_parser("get-jobs")
    jobs_parser.add_argument("--config", default="config/config.yaml")
    jobs_parser.add_argument("--limit", type=int, default=100)

    dev_parser = subparsers.add_parser("get-job-devices")
    dev_parser.add_argument("--job-id", required=True)
    dev_parser.add_argument("--config", default="config/config.yaml")

    del_job_parser = subparsers.add_parser("delete-job")
    del_job_parser.add_argument("--job-id", required=True)
    del_job_parser.add_argument("--config", default="config/config.yaml")

    clear_hist_parser = subparsers.add_parser("clear-history")
    clear_hist_parser.add_argument("--config", default="config/config.yaml")

    inv_parser = subparsers.add_parser("parse-inventory")
    inv_parser.add_argument("--file", required=True)

    save_inv_parser = subparsers.add_parser("save-inventory")
    save_inv_parser.add_argument("--file", required=True)
    save_inv_parser.add_argument("--json", required=True)

    cfg_parser = subparsers.add_parser("get-config")
    cfg_parser.add_argument("--config", default="config/config.yaml")

    plan_parser = subparsers.add_parser("plan")
    plan_parser.add_argument("--file", required=True)
    plan_parser.add_argument("--config", default="config/config.yaml")

    args = parser.parse_args()

    if args.command == "test-env":
        print(json.dumps(test_environment(), indent=2))
    elif args.command == "get-jobs":
        print(json.dumps(get_jobs(args.config, limit=args.limit), indent=2))
    elif args.command == "get-job-devices":
        print(json.dumps(get_job_devices(args.job_id, args.config), indent=2))
    elif args.command == "delete-job":
        print(json.dumps(delete_job(args.job_id, args.config), indent=2))
    elif args.command == "clear-history":
        print(json.dumps(clear_history(args.config), indent=2))
    elif args.command == "parse-inventory":
        print(json.dumps(parse_inventory(args.file), indent=2))
    elif args.command == "save-inventory":
        print(json.dumps(save_inventory(args.file, args.json), indent=2))
    elif args.command == "get-config":
        print(json.dumps(get_config_json(args.config), indent=2))
    elif args.command == "plan":
        print(json.dumps(create_plan(args.file, args.config), indent=2))
    else:
        parser.print_help()


if __name__ == "__main__":
    main()
