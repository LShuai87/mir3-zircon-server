"""Bind the explicit manual-preview entry to current source, runtimes and receipts."""
import hashlib
import json
import re
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[5]
EXTERNAL = Path("E:/workspace/mir3-ui-work")


def load(path):
    return json.loads(path.read_text("utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def portable(path):
    try:
        return path.relative_to(ROOT).as_posix()
    except ValueError:
        return path.as_posix()


def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", "utf-8")


tasks = load(ROOT / "docs/tasks/official-ingame-ui/tasks.json")
previous = load(HERE.parent / "window-title-bounds/verification.json")
history = {}
for name in [tasks["validationReport"], *tasks["supplementalImplementationReports"]]:
    if ROOT / name != HERE / "verification.json":
        history.update({s["path"]: s["sha256"] for s in load(ROOT / name)["sourceHashes"]})
baseline = load(HERE / "source-before.json")
changes = []
for row in baseline:
    rel, before = row["path"], row["sha256"]
    if sha(HERE / "source-before" / rel) != before or history.get(rel) != before:
        raise ValueError("Baseline mismatch " + rel)
    after = sha(ROOT / rel)
    if before != after:
        changes.append(dict(path=rel, beforeSha256=before, afterSha256=after))
if len(changes) != 6:
    raise ValueError("Expected six reviewed source transitions")
new_sources = ["build/game-ui/Prepare-GameUiPreview.ps1", "tests/Run-GameUiPreviewTests.ps1"]
if any(p in history for p in new_sources):
    raise ValueError("New source already exists in recorded history")
commands, failed = [], []
for path in sorted(HERE.glob("*.receipt.json")):
    receipt = load(path)
    log = Path(receipt["log"])
    item = dict(command=receipt["command"], result="pass" if receipt["exitCode"] == 0 else "fail",
                exitCode=receipt["exitCode"], log=portable(log), logSha256=sha(log))
    (commands if receipt["exitCode"] == 0 else failed).append(item)
if failed:
    raise ValueError("Unexpected failed command; review and describe before finalizing")
builds = {}
launch_files = []
for config in ["debug", "release"]:
    manifest = load(HERE / (config + "-build.json"))
    directory = ROOT.parent / ("Debug" if config == "debug" else "Release") / "Client"
    for entry in [manifest["client"], *manifest["dependencies"]]:
        if sha(directory / entry["path"]) != entry["sha256"]:
            raise ValueError("Runtime drift " + config + entry["path"])
    if manifest["catalog"]["runtimeReady"] is not False:
        raise ValueError("Readiness gate changed")
    if manifest["package"]["sha256"] != previous["package"]["sha256"]:
        raise ValueError("Package changed")
    installed = directory / manifest["package"]["path"]
    if sha(installed) != manifest["package"]["sha256"]:
        raise ValueError("Staged package differs " + config)
    for name, flag in [("Start-OfficialUiPreview.cmd", "--game-ui-preview"), ("Start-LegacyUi.cmd", "--game-ui-legacy")]:
        launcher = directory / name
        expected = '@echo off\r\ncd /d "%~dp0"\r\nstart "" "%~dp0Zircon.exe" ' + flag + "\r\n"
        if launcher.read_bytes() != expected.encode("ascii"):
            raise ValueError("Launcher drift " + str(launcher))
        launch_files.append(launcher)
    launch_files.append(installed)
    builds[config] = dict(buildId=manifest["buildId"], client=manifest["client"])
for key in ["package", "candidate"]:
    if sha(Path(previous[key]["path"])) != previous[key]["sha256"]:
        raise ValueError("External package drift " + key)
directories = [EXTERNAL / ("manual-preview-20261110-" + suffix)
               for suffix in ["startup-v1", "staging-v1", "staging-v2", "fullscene-v1"]]
tested = None
for directory in directories:
    runtime = load(directory / "tested-runtime.json")
    for entry in runtime:
        if sha(ROOT.parent / "Debug/Client" / entry["name"]) != entry["sha256"]:
            raise ValueError("Tested runtime mismatch " + directory.name)
    if tested is not None and runtime != tested:
        raise ValueError("Suites used different runtimes")
    tested = runtime
    if any(p.suffix.lower() in (".png", ".jpg", ".jpeg", ".bmp") for p in directory.rglob("*")):
        raise ValueError("Unexpected screenshot in this round " + str(directory))
write(HERE / "tested-runtime.json", tested)
policy = (directories[-1] / "capture-policy.txt").read_text("utf-8-sig")
if "Screenshots and pixel comparisons disabled" not in policy:
    raise ValueError("No-screenshot policy not applied")
results = {}
for key, log, pattern in [
    ("startupChecks", "startup.log", r"Startup selection checks passed: (\d+)"),
    ("stagingChecks", "staging-final.log", r"Manual-preview staging: (\d+) checks passed"),
    ("fullSceneChecks", "full-scene.log", r"Full-scene offline integration: (\d+) checks passed")]:
    match = re.search(pattern, (HERE / log).read_text("utf-8", errors="replace"))
    if not match:
        raise ValueError("Missing suite result " + key)
    results[key] = int(match.group(1))
if results != dict(startupChecks=368, stagingChecks=15, fullSceneChecks=4297):
    raise ValueError("Unexpected suite counts " + str(results))
if sha(ROOT / tasks["sourcePlan"]) != tasks["sourcePlanSha256"]:
    raise ValueError("Immutable plan changed")
report = dict(
    schemaVersion=1, kind="official-manual-preview-entry-verification", checkedAtLocalDate="2026-11-10",
    referenceResolution=[1024, 768], releaseAccepted=False,
    results=dict(suites=results, screenshotsCaptured=False, pixelComparisonsPerformed=False,
                 productionSourceChanged=True, realServerBehaviorVerified=False, finalVisualAcceptance=False),
    implementation=[
        "Add exact per-process preview and Legacy flags at the normal application entry point; persisted configuration and compiled production gate remain unchanged.",
        "Explicit preview retains the complete CPU/GPU package preflight, fallback diagnostics and application-owned resource lifecycle.",
        "Add conflict-preserving local staging with validation-only mode and two launchers; reject the official game installation.",
        "Add screenshot-free full-scene mode that retains rendering and functional checks, and test through the public preview entry."],
    verifiedBehaviors=[
        "368 startup checks include missing/corrupt package fallback, unknown/conflicting flags, config retention and three full 94-frame GPU lifetimes.",
        "15 staging checks cover validation without writes, conflict retention, source=destination, idempotency, INI retention and official-directory rejection. Final rerun follows incomplete-copy cleanup refinement.",
        "4297 full-scene checks cover 27 constructors, 47 presented frames and 9 explicit device resets without saving screenshots or comparing pixels.",
        "Debug and Release compile; both contain hash-matching candidate packages and explicit launchers. Debug is the manual review entry with existing base assets/configuration."],
    sourceChanges=changes, firstTrackedSourceChanges=[], newlyTrackedSources=new_sources,
    sourceHashes=[dict(path=rel, sha256=sha(ROOT / rel)) for rel in [r["path"] for r in baseline] + new_sources],
    commands=commands, failedAttempts=failed, builds=builds, runtimeRebuiltThisTurn=True,
    runtimeManifestReferences={c: portable(HERE / (c + "-build.json")) for c in ["debug", "release"]},
    package=previous["package"], candidate=previous["candidate"], sourcePlanSha256=tasks["sourcePlanSha256"],
    limitations=[
        "User requested manual visual review. No screenshot comparison or final appearance acceptance is claimed.",
        "Synthetic offline loopback tests do not prove real-server transactions, live IME, hardware device-loss or zero bugs.",
        "Release output has the built runtime and candidate package but lacks base game assets/settings; use the configured Debug entry for manual review.",
        "Tasks remain 7 done / 3 in_progress / 18 not_started; G0-2/3/4/5 remain open. Default Legacy, RuntimeReady=false, productionBindingAllowed=false and releaseAccepted=false.",
        "NPC interaction cards, secondary/service/confirmation windows, settings and login/selection retain native style. Official installation is read-only; no commit, push or deployment."])
paths = {p for directory in directories for p in directory.rglob("*") if p.is_file()}
paths.update(launch_files)
paths.update(p for p in HERE.rglob("*") if p.is_file() and p != HERE / "verification.json")
report["evidenceHashes"] = [dict(path=portable(p), sha256=sha(p)) for p in sorted(paths)]
write(HERE / "verification.json", report)
print(json.dumps(dict(sourceChanges=len(changes), newSources=len(new_sources), successfulCommands=len(commands),
                      evidenceFiles=len(paths), results=results), indent=2))
