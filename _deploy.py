#!/usr/bin/env python3
"""
=============================================================
  AbroadQs Telegram Bot - Deployment Script
=============================================================
  This script deploys the bot to the remote server via SSH.
  It pulls the latest code from GitHub, builds Docker images,
  and restarts the containers.

  Usage:
    python _deploy.py                  # Full deploy (pull + build + restart)
    python _deploy.py --logs           # Show last 50 lines of bot logs
    python _deploy.py --status         # Show container status
    python _deploy.py --restart        # Restart bot & tunnel without rebuild
    python _deploy.py --rebuild-all    # Rebuild ALL containers (including infra)
    python _deploy.py --rollback       # Rollback to previous git commit
    python _deploy.py --shell "cmd"    # Run a custom command on server
    python _deploy.py --health         # Full health check of all services

  Requirements:
    pip install paramiko
=============================================================
"""

import sys
import time
import argparse

try:
    import paramiko
except ImportError:
    print("ERROR: paramiko is not installed.")
    print("Install it with: pip install paramiko")
    sys.exit(1)

# ─────────────────────────────────────────────────────────────
# Server Configuration - UPDATE THESE IF YOUR SERVER CHANGES
# ─────────────────────────────────────────────────────────────
SERVER_HOST = "167.235.159.117"
SERVER_PORT = 2200
SERVER_USER = "root"
SERVER_PASS = "Kia135724!"

# Project paths on the server
REPO_DIR = "/root/AbroadQs.TelegramBot"
PROJECT_DIR = f"{REPO_DIR}/AbroadQs.TelegramBot"
COMPOSE_FILE = "docker-compose.server.yml"

# GitHub
GIT_BRANCH = "main"
GIT_REMOTE = "origin"

# Container names
CONTAINER_BOT = "abroadqs-bot"
CONTAINER_TUNNEL = "abroadqs-tunnel"
CONTAINER_REDIS = "abroadqs-redis"
CONTAINER_RABBIT = "abroadqs-rabbitmq"
CONTAINER_SQL = "abroadqs-sqlserver"

# ─────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────

class Colors:
    """ANSI color codes for terminal output."""
    GREEN = "\033[92m"
    RED = "\033[91m"
    YELLOW = "\033[93m"
    CYAN = "\033[96m"
    BOLD = "\033[1m"
    DIM = "\033[2m"
    RESET = "\033[0m"

def print_header(text):
    print(f"\n{Colors.CYAN}{'=' * 60}")
    print(f"  {Colors.BOLD}{text}{Colors.RESET}")
    print(f"{Colors.CYAN}{'=' * 60}{Colors.RESET}")

def print_step(step_num, total, text):
    print(f"\n{Colors.YELLOW}[{step_num}/{total}]{Colors.RESET} {Colors.BOLD}{text}{Colors.RESET}")

def print_success(text):
    print(f"{Colors.GREEN}  OK: {text}{Colors.RESET}")

def print_error(text):
    print(f"{Colors.RED}  ERROR: {text}{Colors.RESET}")

def print_warning(text):
    print(f"{Colors.YELLOW}  WARNING: {text}{Colors.RESET}")

def print_info(text):
    print(f"{Colors.DIM}  {text}{Colors.RESET}")

def connect_ssh():
    """Establish SSH connection to the server."""
    print_info(f"Connecting to {SERVER_HOST}:{SERVER_PORT} as {SERVER_USER}...")
    ssh = paramiko.SSHClient()
    ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    try:
        ssh.connect(
            SERVER_HOST,
            port=SERVER_PORT,
            username=SERVER_USER,
            password=SERVER_PASS,
            timeout=30,
            banner_timeout=30,
            auth_timeout=30
        )
        print_success("SSH connection established")
        return ssh
    except paramiko.ssh_exception.AuthenticationException:
        print_error("Authentication failed! Check SERVER_USER and SERVER_PASS.")
        sys.exit(1)
    except paramiko.ssh_exception.NoValidConnectionsError:
        print_error(f"Cannot connect to {SERVER_HOST}:{SERVER_PORT}. Is the server running?")
        sys.exit(1)
    except Exception as e:
        print_error(f"SSH connection failed: {e}")
        print_info("TIP: Try again in a few seconds. If persistent, check your network / server firewall.")
        sys.exit(1)

def run_remote(ssh, command, timeout=300, show_output=True, fail_ok=False):
    """Execute a command on the remote server and return (exit_code, stdout, stderr)."""
    stdin, stdout, stderr = ssh.exec_command(command, timeout=timeout)
    exit_code = stdout.channel.recv_exit_status()
    out = stdout.read().decode("utf-8", errors="replace").strip()
    err = stderr.read().decode("utf-8", errors="replace").strip()

    if show_output:
        if out:
            for line in out.split("\n"):
                print(f"  {line}")
        if err:
            for line in err.split("\n"):
                # Git writes to stderr for normal progress
                if exit_code == 0:
                    print(f"{Colors.DIM}  {line}{Colors.RESET}")
                else:
                    print(f"{Colors.RED}  {line}{Colors.RESET}")

    if exit_code != 0 and not fail_ok:
        print_error(f"Command failed with exit code {exit_code}")
        if not show_output and err:
            print(f"{Colors.RED}  {err}{Colors.RESET}")

    return exit_code, out, err

# ─────────────────────────────────────────────────────────────
# Deploy Actions
# ─────────────────────────────────────────────────────────────

def do_full_deploy(ssh):
    """Full deployment: git pull -> docker build -> docker up -> verify."""
    total_steps = 7
    print_header("FULL DEPLOYMENT")

    # Step 1: Git pull
    print_step(1, total_steps, "Pulling latest code from GitHub")
    code, out, err = run_remote(ssh, f"cd {PROJECT_DIR} && git pull {GIT_REMOTE} {GIT_BRANCH}")
    if code != 0:
        print_error("Git pull failed!")
        print_info("Possible causes:")
        print_info("  - Network issue on server")
        print_info("  - Merge conflicts (run: python _deploy.py --shell \"cd /root/AbroadQs.TelegramBot/AbroadQs.TelegramBot && git status\")")
        print_info("  - GitHub authentication expired")
        return False
    if "Already up to date" in out:
        print_warning("No new changes from GitHub. Rebuilding anyway...")
    else:
        print_success("Code updated successfully")

    # Step 2: Show what changed
    print_step(2, total_steps, "Recent git log")
    run_remote(ssh, f"cd {PROJECT_DIR} && git log --oneline -5")

    # Step 3: Build Docker images (bot + tunnel only)
    print_step(3, total_steps, "Building Docker images (bot + tunnel)")
    print_info("This may take 30-60 seconds...")
    code, _, _ = run_remote(ssh, f"cd {PROJECT_DIR} && docker compose -f {COMPOSE_FILE} build bot tunnel", timeout=600)
    if code != 0:
        print_error("Docker build failed!")
        print_info("Possible causes:")
        print_info("  - Code compilation error (check output above)")
        print_info("  - Disk space full (run: python _deploy.py --shell \"df -h\")")
        print_info("  - Docker daemon issue (run: python _deploy.py --shell \"systemctl status docker\")")
        return False
    print_success("Docker images built successfully")

    # Step 4: Restart containers
    print_step(4, total_steps, "Restarting bot & tunnel containers")
    code, _, _ = run_remote(ssh, f"cd {PROJECT_DIR} && docker compose -f {COMPOSE_FILE} up -d bot tunnel")
    if code != 0:
        print_error("Failed to start containers!")
        return False
    print_success("Containers restarted")

    # Step 5: Wait for startup
    print_step(5, total_steps, "Waiting for application startup (10s)")
    time.sleep(10)

    # Step 6: Check container status
    print_step(6, total_steps, "Container status")
    run_remote(ssh, f"cd {PROJECT_DIR} && docker compose -f {COMPOSE_FILE} ps")

    # Step 7: Check for errors
    print_step(7, total_steps, "Checking for startup errors")
    code, out, _ = run_remote(ssh,
        f'docker logs {CONTAINER_BOT} --tail 200 2>&1 | grep -iE "error|exception|fail|fatal" | grep -vi "warning" | tail -10',
        fail_ok=True, show_output=False)
    if out.strip():
        print_warning("Potential errors found in logs:")
        for line in out.strip().split("\n"):
            print(f"  {Colors.RED}{line}{Colors.RESET}")
    else:
        print_success("No errors found in recent logs")

    # Show last few log lines
    print(f"\n{Colors.DIM}--- Last log lines ---{Colors.RESET}")
    run_remote(ssh, f"docker logs {CONTAINER_BOT} --tail 10 2>&1")

    print_header("DEPLOYMENT COMPLETE")
    print_success("Bot is running at https://webhook.abroadqs.com")
    print_success("Dashboard at https://webhook.abroadqs.com/dashboard")
    return True


def do_restart(ssh):
    """Restart bot & tunnel without rebuilding."""
    print_header("RESTART CONTAINERS")
    print_step(1, 3, "Restarting bot & tunnel")
    run_remote(ssh, f"cd {PROJECT_DIR} && docker compose -f {COMPOSE_FILE} restart bot tunnel")
    print_step(2, 3, "Waiting for startup (10s)")
    time.sleep(10)
    print_step(3, 3, "Container status")
    run_remote(ssh, f"cd {PROJECT_DIR} && docker compose -f {COMPOSE_FILE} ps")
    print_success("Restart complete")


def do_rebuild_all(ssh):
    """Rebuild ALL containers including infrastructure."""
    print_header("REBUILD ALL CONTAINERS")
    print_warning("This will rebuild ALL containers including Redis, RabbitMQ, SQL Server!")
    print_warning("Data volumes will be preserved, but there will be brief downtime.")

    print_step(1, 4, "Pulling latest code")
    run_remote(ssh, f"cd {PROJECT_DIR} && git pull {GIT_REMOTE} {GIT_BRANCH}")

    print_step(2, 4, "Stopping all containers")
    run_remote(ssh, f"cd {PROJECT_DIR} && docker compose -f {COMPOSE_FILE} down")

    print_step(3, 4, "Building and starting all containers")
    run_remote(ssh, f"cd {PROJECT_DIR} && docker compose -f {COMPOSE_FILE} up -d --build", timeout=600)

    print_step(4, 4, "Waiting for all services (30s)")
    time.sleep(30)

    run_remote(ssh, f"cd {PROJECT_DIR} && docker compose -f {COMPOSE_FILE} ps")
    print_success("All containers rebuilt and running")


def do_rollback(ssh):
    """Rollback to the previous git commit."""
    print_header("ROLLBACK TO PREVIOUS VERSION")

    print_step(1, 5, "Current commit")
    run_remote(ssh, f"cd {PROJECT_DIR} && git log --oneline -3")

    print_step(2, 5, "Rolling back one commit")
    code, _, _ = run_remote(ssh, f"cd {PROJECT_DIR} && git reset --hard HEAD~1")
    if code != 0:
        print_error("Rollback failed!")
        return

    print_step(3, 5, "New HEAD commit")
    run_remote(ssh, f"cd {PROJECT_DIR} && git log --oneline -1")

    print_step(4, 5, "Rebuilding Docker images")
    run_remote(ssh, f"cd {PROJECT_DIR} && docker compose -f {COMPOSE_FILE} build bot tunnel", timeout=600)

    print_step(5, 5, "Restarting containers")
    run_remote(ssh, f"cd {PROJECT_DIR} && docker compose -f {COMPOSE_FILE} up -d bot tunnel")
    time.sleep(10)
    run_remote(ssh, f"cd {PROJECT_DIR} && docker compose -f {COMPOSE_FILE} ps")
    print_success("Rollback complete")
    print_warning("NOTE: Server is now behind GitHub. Next deploy will pull the latest again.")
    print_info("To keep this version, force push: git push origin main --force (CAREFUL!)")


def do_logs(ssh, lines=50):
    """Show recent bot logs."""
    print_header(f"BOT LOGS (last {lines} lines)")
    run_remote(ssh, f"docker logs {CONTAINER_BOT} --tail {lines} 2>&1")


def do_status(ssh):
    """Show container status."""
    print_header("CONTAINER STATUS")
    run_remote(ssh, f"cd {PROJECT_DIR} && docker compose -f {COMPOSE_FILE} ps")

    print(f"\n{Colors.BOLD}Disk Usage:{Colors.RESET}")
    run_remote(ssh, "df -h / | tail -1")

    print(f"\n{Colors.BOLD}Memory Usage:{Colors.RESET}")
    run_remote(ssh, "free -h | head -2")

    print(f"\n{Colors.BOLD}Docker Disk Usage:{Colors.RESET}")
    run_remote(ssh, "docker system df")


def do_health(ssh):
    """Full health check of all services."""
    print_header("HEALTH CHECK")

    services = [
        ("Bot Container", f"docker inspect --format='{{{{.State.Status}}}}' {CONTAINER_BOT} 2>/dev/null || echo 'NOT FOUND'"),
        ("Tunnel Container", f"docker inspect --format='{{{{.State.Status}}}}' {CONTAINER_TUNNEL} 2>/dev/null || echo 'NOT FOUND'"),
        ("Redis Container", f"docker inspect --format='{{{{.State.Status}}}}' {CONTAINER_REDIS} 2>/dev/null || echo 'NOT FOUND'"),
        ("RabbitMQ Container", f"docker inspect --format='{{{{.State.Status}}}}' {CONTAINER_RABBIT} 2>/dev/null || echo 'NOT FOUND'"),
        ("SQL Server Container", f"docker inspect --format='{{{{.State.Status}}}}' {CONTAINER_SQL} 2>/dev/null || echo 'NOT FOUND'"),
        ("Nginx Status", "systemctl is-active nginx 2>/dev/null || echo 'NOT RUNNING'"),
        ("SSL Certificate", "certbot certificates 2>/dev/null | grep -A2 'webhook.abroadqs.com' | grep 'Expiry' || echo 'No cert info'"),
        ("Webhook URL Test", "curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:5252/health 2>/dev/null || echo 'UNREACHABLE'"),
        ("Disk Space", "df -h / | tail -1"),
        ("Memory", "free -h | grep Mem"),
    ]

    for name, cmd in services:
        _, out, _ = run_remote(ssh, cmd, show_output=False, fail_ok=True)
        status = out.strip() if out.strip() else "unknown"
        if status in ("running", "active", "200"):
            print(f"  {Colors.GREEN}[OK]{Colors.RESET}  {name}: {status}")
        elif "NOT" in status or "unknown" in status:
            print(f"  {Colors.RED}[!!]{Colors.RESET}  {name}: {status}")
        else:
            print(f"  {Colors.YELLOW}[??]{Colors.RESET}  {name}: {status}")

    # Check for error logs
    print(f"\n{Colors.BOLD}Recent Error Logs:{Colors.RESET}")
    _, out, _ = run_remote(ssh,
        f'docker logs {CONTAINER_BOT} --tail 500 2>&1 | grep -iE "error|exception|fatal" | grep -vi "warning" | tail -5',
        fail_ok=True, show_output=False)
    if out.strip():
        for line in out.strip().split("\n"):
            print(f"  {Colors.RED}{line}{Colors.RESET}")
    else:
        print_success("No recent errors")


def do_shell(ssh, command):
    """Run a custom command on the server."""
    print_header(f"CUSTOM COMMAND")
    print_info(f"$ {command}")
    run_remote(ssh, command, timeout=60)


# ─────────────────────────────────────────────────────────────
# Main
# ─────────────────────────────────────────────────────────────

def main():
    parser = argparse.ArgumentParser(
        description="AbroadQs Telegram Bot - Deployment Tool",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
Examples:
  python _deploy.py                    # Full deploy
  python _deploy.py --logs             # Show bot logs
  python _deploy.py --logs 100         # Show last 100 log lines
  python _deploy.py --status           # Container status
  python _deploy.py --restart          # Restart without rebuild
  python _deploy.py --rebuild-all      # Rebuild everything
  python _deploy.py --rollback         # Revert to previous commit
  python _deploy.py --health           # Full health check
  python _deploy.py --shell "df -h"    # Run custom command
        """
    )

    group = parser.add_mutually_exclusive_group()
    group.add_argument("--logs", nargs="?", const=50, type=int, metavar="LINES",
                       help="Show bot logs (default: 50 lines)")
    group.add_argument("--status", action="store_true",
                       help="Show container and server status")
    group.add_argument("--restart", action="store_true",
                       help="Restart bot & tunnel without rebuilding")
    group.add_argument("--rebuild-all", action="store_true",
                       help="Rebuild ALL containers (including infra)")
    group.add_argument("--rollback", action="store_true",
                       help="Rollback to previous git commit")
    group.add_argument("--health", action="store_true",
                       help="Full health check of all services")
    group.add_argument("--shell", type=str, metavar="CMD",
                       help="Run a custom command on the server")

    args = parser.parse_args()

    # Connect
    ssh = connect_ssh()

    try:
        if args.logs is not None:
            do_logs(ssh, args.logs)
        elif args.status:
            do_status(ssh)
        elif args.restart:
            do_restart(ssh)
        elif args.rebuild_all:
            do_rebuild_all(ssh)
        elif args.rollback:
            do_rollback(ssh)
        elif args.health:
            do_health(ssh)
        elif args.shell:
            do_shell(ssh, args.shell)
        else:
            # Default: full deploy
            do_full_deploy(ssh)
    except KeyboardInterrupt:
        print(f"\n{Colors.YELLOW}Interrupted by user.{Colors.RESET}")
    except Exception as e:
        print_error(f"Unexpected error: {e}")
    finally:
        ssh.close()
        print_info("SSH connection closed.")


if __name__ == "__main__":
    main()
