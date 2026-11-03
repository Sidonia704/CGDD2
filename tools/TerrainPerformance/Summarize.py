#!/usr/bin/env python3
"""Read-only report analysis; writes only the requested summary, README and hash audit.

Run from any directory. No Unity invocation, runtime edits, asset writes or FPS inference.
Inputs may be files, directories or glob patterns; unknown JSON layouts are summarized by
numeric field path. The five optimized-matched VerifyAssets runs are kept in their own
group, separate from complementary profiler and prewarm reports. No speedup is calculated
across unlike probes and no missing value is ever replaced by zero.
"""
from __future__ import annotations

import argparse
from collections import defaultdict
from datetime import datetime, timezone
import glob
import hashlib
import json
import math
from pathlib import Path
import re
import statistics
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
DOCS = Path("Documentation/TerrainPerformance-2026-11-01")
PROFILE_DIR = DOCS / "optimized"
PREWARM_FILE = DOCS / "prewarm-audit.json"
ALLOCATION_AUDIT_FILE = DOCS / "allocation-counter-audit.json"
LEASE_AUDIT_FILE = DOCS / "matched-cache-lease-audit.json"
MATCHED_DIR = DOCS / "optimized-matched"
TESTS_DIR = DOCS / "tests"
MATCHED_RUN_COUNT = 5
MATCHED_RUNS = tuple(MATCHED_DIR / f"run-{index}.json" for index in range(MATCHED_RUN_COUNT))
TEST_COUNT_KEYS = ("Total", "Passed", "Failed", "Skipped", "Inconclusive")
PROFILE_VERSIONS = ("terrain-performance-1", "terrain-performance-2")
IDENTITY_KEYS = ("probeId", "testName", "name", "assetName", "asset", "probe", "scenario", "label", "id")
CONTEXT_KEYS = ("role", "scenario", "phase", "stage", "cacheState", "cacheMode", "mode", "variant")
SAMPLE_LISTS = {"samples", "runs", "iterations", "hits", "measurements", "timings"}


def utc_now():
    return datetime.now(timezone.utc).isoformat()


def resolve(value):
    path = Path(value)
    return path.resolve() if path.is_absolute() else (ROOT / path).resolve()


def relative(path):
    try:
        return path.resolve().relative_to(ROOT).as_posix()
    except ValueError:
        return path.resolve().as_posix()


def link(path, label=None):
    path = resolve(path)
    # Full project-relative paths, not paths relative to the README's directory.
    return f"[{label or path.name}](<{relative(path)}>)"


def reject_constant(value):
    raise ValueError(f"Non-finite JSON value: {value}")


def finite_float(value):
    number = float(value)
    if not math.isfinite(number):
        raise ValueError(f"Non-finite JSON number: {value}")
    return number


def load_json(path):
    """Read JSON reports; Unity CLI responses are UTF-16, project reports are UTF-8."""
    raw = path.read_bytes()
    encoding = "utf-16" if raw[:2] in (b"\xff\xfe", b"\xfe\xff") else "utf-8-sig"
    return json.loads(raw.decode(encoding), parse_constant=reject_constant,
                      parse_float=finite_float)


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False, allow_nan=False) + "\n", encoding="utf-8")


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def run_index(path):
    """Retain the declared run-<n> index; None means the file name declares none."""
    match = re.fullmatch(r"run-(\d+)", path.stem)
    return int(match.group(1)) if match else None


def numeric(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def stats(values):
    values = list(values)
    if not values or not all(numeric(value) for value in values):
        raise ValueError("Statistics require nonempty finite numeric samples")
    return {"n": len(values), "median": statistics.median(values), "min": min(values),
            "max": max(values), "samples": values}


def expand_inputs(specs, required=True):
    """Discover only explicitly selected inputs; never mix old v04 reports by default."""
    found = set()
    for spec in specs:
        pattern = str(resolve(spec))
        paths = [Path(p) for p in glob.glob(pattern, recursive=True)]
        if not paths and required:
            raise ValueError(f"Input does not exist or match any files: {spec}")
        for path in paths:
            if path.is_dir():
                found.update(p.resolve() for p in path.rglob("*.json") if p.is_file())
            elif path.is_file() and path.suffix.lower() == ".json":
                found.add(path.resolve())
    if not found and required:
        raise ValueError("No JSON reports selected")
    return sorted(found)


def aggregate_records(records):
    """Retain missing measurements as missing, never substitute zero."""
    keys = sorted({key for record in records for key in record})
    metrics, flags, labels = {}, {}, {}
    for key in keys:
        values = [record[key] for record in records if key in record and record[key] is not None]
        if values and all(numeric(v) for v in values):
            metrics[key] = stats(values)
        elif values and all(isinstance(v, bool) for v in values):
            flags[key] = {"n": len(values), "true": sum(values), "false": len(values) - sum(values)}
        elif values and all(isinstance(v, str) for v in values):
            labels[key] = sorted(set(values))
    return {"recordCount": len(records), "metrics": metrics, "flags": flags, "labels": labels}


def named_records(reports, section, identity):
    grouped = defaultdict(list)
    for report in reports:
        seen = set()
        for record in report.get(section, []):
            key = record[identity]
            if key in seen:
                raise ValueError(f"Duplicate {section} identity in one report: {key}")
            seen.add(key)
            grouped[key].append(record)
    return {name: aggregate_records(records) for name, records in sorted(grouped.items())}


def load_verify_reports(paths, label="original"):
    """Load VerifyAssets reports once; reject any file that is not that exact schema."""
    pairs = []
    for path in paths:
        report = load_json(path)
        if not isinstance(report, dict) or report.get("version") != "mining-round2-verification/1":
            raise ValueError(f"Not a VerifyAssets report ({label}): {relative(path)}")
        pairs.append((path, report))
    return pairs


def summarize_verify_pairs(pairs):
    """Summarize the same VerifyAssets probe family without assuming warm or cold cache."""
    reports = [report for _, report in pairs]
    return {
        "reportCount": len(reports),
        "reports": [{"path": relative(path), "sha256": sha256(path), "runIndex": run_index(path),
                     "generatedUtc": report.get("generatedUtc"), "passed": report.get("passed"),
                     "durationMs": report.get("durationMs"),
                     "failures": report.get("failures", []), "unsupported": report.get("unsupported", [])}
                    for path, report in pairs],
        "allReportsPassed": all(r.get("passed") is True and not r.get("failures") and
                                not r.get("unsupported") for r in reports),
        "environment": aggregate_records([r.get("environment", {}) for r in reports]),
        "assets": named_records(reports, "assets", "name"),
        "singleCellDamage": named_records(reports, "representatives", "assetName"),
        **{key: aggregate_records([r.get(key, {}) for r in reports])
           for key in ("cut", "density", "physics", "patchPhysics", "layout", "source")},
        "reportDurationMs": stats(r["durationMs"] for r in reports),
        "phaseAvailability": {
            "coldPrewarm": {"measured": False, "reason": "This VerifyAssets probe never clears the template cache; all firstBuildMs values must retain their per-asset cache flags."},
            "initialization": {"measured": True, "cache": "warm template", "fields": ["assets[].firstBuildMs", "assets[].repeatBuildMs", "patchPhysics.firstPerAssetInitMs", "patchPhysics.warmInitMs", "patchPhysics.initTotalMs"]},
            "firstLethalHit": {"measured": True, "fields": ["representatives[].applyDamageMs", "representatives[].flushMs", "representatives[].damageMs"], "scope": "one lethal surface-cell hit per fresh representative instance; no sustained stream"},
            "sustainedHits": {"measured": False, "reason": "Five independent one-hit probes are not five sustained hits on the same body."},
        },
        "limits": sorted({text for r in reports for text in r.get("limits", [])}),
    }


def summarize_baseline(paths, label="original"):
    return summarize_verify_pairs(load_verify_reports(paths, label))


def verify_identities(report):
    """Only identities are compared; counts and timings legitimately differ after a fix."""
    assets, representatives, cut = report.get("assets"), report.get("representatives"), report.get("cut")
    return {
        "assetNames": sorted(r["name"] for r in assets
                             if isinstance(r, dict) and isinstance(r.get("name"), str)) if isinstance(assets, list) else [],
        "representativeAssets": sorted(r["assetName"] for r in representatives
                                       if isinstance(r, dict) and isinstance(r.get("assetName"), str)) if isinstance(representatives, list) else [],
        "cutAsset": cut.get("assetName") if isinstance(cut, dict) and isinstance(cut.get("assetName"), str) else None,
        "layoutPath": report.get("layoutPath") if isinstance(report.get("layoutPath"), str) else None,
    }


def baseline_identities(baseline):
    cut_labels, layout_labels = baseline["cut"].get("labels", {}), baseline["layout"].get("labels", {})
    return {
        "assetNames": sorted(baseline["assets"]),
        "representativeAssets": sorted(baseline["singleCellDamage"]),
        "cutAsset": (cut_labels.get("assetName") or [None])[0],
        "layoutPath": (layout_labels.get("path") or [None])[0],
    }


def delta_rows(baseline_group, matched_group, phase, identity):
    """Subtract only metrics measured in both groups; never invent a baseline value."""
    baseline_metrics, matched_metrics = baseline_group.get("metrics", {}), matched_group.get("metrics", {})
    rows = []
    for metric in sorted(set(baseline_metrics) & set(matched_metrics)):
        before, after = baseline_metrics[metric], matched_metrics[metric]
        rows.append({"phase": phase, "identity": identity, "metric": metric,
                     "baselineMedian": before["median"], "matchedMedian": after["median"],
                     "delta": after["median"] - before["median"],
                     "baselineN": before["n"], "matchedN": after["n"]})
    return rows


def metric_coverage(baseline_group, matched_group, phase, identity):
    baseline_metrics, matched_metrics = set(baseline_group.get("metrics", {})), set(matched_group.get("metrics", {}))
    coverage = {"phase": phase, "identity": identity,
                "baselineOnly": sorted(baseline_metrics - matched_metrics),
                "matchedOnly": sorted(matched_metrics - baseline_metrics)}
    return coverage if coverage["baselineOnly"] or coverage["matchedOnly"] else None


def compare_matched(baseline, matched):
    """Describe same-probe CPU-ms differences only when five compatible runs exist."""
    verify = matched.get("verify")
    if not verify:
        return {"status": "NOT_YET_MEASURED", "reason": "No matched optimized VerifyAssets report is present."}
    if not matched["complete"]:
        return {"status": "NOT_YET_MEASURED", "reason": matched["statusReason"],
                "presentRuns": matched["presentRuns"], "missingRuns": matched["missingRuns"]}
    if not matched["compatible"]:
        return {"status": "NOT_COMPARABLE", "reason": matched["statusReason"],
                "identityDifferences": matched["identityDifferences"]}
    if not verify["allReportsPassed"]:
        return {"status": "NOT_COMPARABLE", "reason": "At least one matched report failed or reported unsupported fields; no delta is derived from failed probes."}
    if not baseline["allReportsPassed"]:
        return {"status": "NOT_COMPARABLE", "reason": "At least one baseline report failed; no delta is derived from failed probes."}
    deltas, coverage = [], []
    for phase in ("assets", "singleCellDamage"):
        for identity in sorted(set(baseline[phase]) & set(verify[phase])):
            deltas += delta_rows(baseline[phase][identity], verify[phase][identity], phase, identity)
            one = metric_coverage(baseline[phase][identity], verify[phase][identity], phase, identity)
            if one:
                coverage.append(one)
    for phase in ("cut", "density", "physics", "patchPhysics", "layout", "source"):
        deltas += delta_rows(baseline[phase], verify[phase], phase, None)
        one = metric_coverage(baseline[phase], verify[phase], phase, None)
        if one:
            coverage.append(one)
    duration_before, duration_after = baseline.get("reportDurationMs"), verify.get("reportDurationMs")
    duration_exception = None
    if duration_before and duration_after:
        duration_exception = {"phase": "report", "metric": "durationMs",
                              "baselineMedian": duration_before["median"],
                              "matchedMedian": duration_after["median"],
                              "deltaSuppressed": True,
                              "reason": "Whole-report duration includes different explicit warm-lease preparation and cleanup work from the preserved baseline; it is not a matched speedup metric."}
    return {
        "status": "DESCRIPTIVE_ONLY",
        "deltaConvention": "matchedMedian - baselineMedian; positive means the optimized run was slower on that CPU-ms/count field.",
        "baselineReportCount": baseline["reportCount"], "matchedReportCount": verify["reportCount"],
        "baselineAllPassed": baseline["allReportsPassed"], "matchedAllPassed": verify["allReportsPassed"],
        "unchangedIdentities": True,
        "deltas": deltas, "metricCoverage": coverage,
        "durationComparabilityException": duration_exception,
        "limitations": [
            "Only the same five VerifyAssets probes and the same named assets are subtracted; unlike layouts are never mixed.",
            "Fields measured in only one group are listed in metricCoverage and are not given a fabricated delta.",
            "Warm-cache Editor CPU milliseconds, not frame times, FPS, target budgets or a map-assembly approval.",
            "First-hit totals include chunk-renderer activation and draw-call setup; prewarm native mesh memory is measured separately and is outside these numbers.",
            "Whole-report duration is not differenced because explicit warm-lease preparation/cleanup work differs from the baseline; raw per-group durations remain descriptive only.",
            "No benefit is claimed until the matched reports and lease audit exist, pass and functional tests are recorded.",
        ]}


def summarize_matched(paths, baseline, missing_expected):
    """The five directly matched optimized VerifyAssets runs; never merged with profiles."""
    expected = [relative(resolve(spec)) for spec in MATCHED_RUNS]
    present = [relative(path) for path in paths]
    result = {"status": "NOT_YET_MEASURED",
              "statusReason": "No matched optimized VerifyAssets report is present yet; nothing is inferred or substituted.",
              "complete": False, "compatible": None, "expectedRuns": expected,
              "presentRuns": present, "missingRuns": list(missing_expected or expected),
              "reportCount": 0, "allReportsPassed": None, "identityDifferences": [],
              "verify": None, "comparison": None}
    if not paths:
        result["comparison"] = compare_matched(baseline, result)
        return result
    pairs = load_verify_reports(paths, "matched optimized")
    declared = [run_index(path) for path, _ in pairs]
    known = [index for index in declared if index is not None]
    if len(known) != len(set(known)):
        raise ValueError("Duplicate matched run index; each run-<n>.json may appear only once")
    pairs.sort(key=lambda pair: (run_index(pair[0]) is None, run_index(pair[0]) or 0, pair[0].name))
    verify = summarize_verify_pairs(pairs)
    expected_identities = baseline_identities(baseline)
    differences = []
    for path, report in pairs:
        actual = verify_identities(report)
        for field, expected_value in expected_identities.items():
            if actual.get(field) != expected_value:
                differences.append({"run": relative(path), "field": field,
                                    "baseline": expected_value, "matched": actual.get(field)})
    complete = len(paths) == MATCHED_RUN_COUNT and not missing_expected
    if len(paths) > MATCHED_RUN_COUNT:
        status, reason = "UNEXPECTED_RUN_COUNT", f"{len(paths)} matched reports present; exactly {MATCHED_RUN_COUNT} are required."
    elif not complete:
        status, reason = "PARTIAL_IN_PROGRESS", f"{len(paths)} of {MATCHED_RUN_COUNT} matched runs present; no comparison is made from an incomplete set."
    elif differences:
        status, reason = "INCOMPATIBLE_WITH_BASELINE", "Matched probe identities differ from the fresh baseline; no comparison is made."
    elif not verify["allReportsPassed"]:
        status, reason = "READY_WITH_FAILURES", "All five matched reports are present but at least one failed or reported unsupported fields."
    else:
        status, reason = "READY_FOR_REVIEW", "All five matched reports are present and passed; the separate test and benefit gates decide the final label."
    result.update({"status": status, "statusReason": reason, "complete": complete,
                   "compatible": not differences, "reportCount": verify["reportCount"],
                   "allReportsPassed": verify["allReportsPassed"],
                   "identityDifferences": differences, "missingRuns": list(missing_expected or []),
                   "verify": verify})
    result["comparison"] = compare_matched(baseline, result)
    return result


def summarize_lease_audit(paths):
    """Guarded matched warm-cache lease audit: 10 leases, all ownership restored."""
    reports, passing = [], 0
    for path in paths:
        report = load_json(path)
        reasons = []
        rows = report.get("rows") if isinstance(report, dict) and isinstance(report.get("rows"), list) else []
        explicit = report.get("explicitWarmLeases") if isinstance(report, dict) else None
        preparation = report.get("warmLeasePreparationMs") if isinstance(report, dict) else None
        restored = report.get("ownershipRestored") if isinstance(report, dict) else None
        if not isinstance(explicit, int) or isinstance(explicit, bool) or explicit != 10:
            reasons.append(f"explicitWarmLeases is {explicit}; exactly 10 are required")
        if not numeric(preparation) or preparation < 0:
            reasons.append(f"warmLeasePreparationMs is {preparation}; a non-negative measurement is required")
        if restored is not True:
            reasons.append("Top-level ownershipRestored is not true")
        if len(rows) != 10:
            reasons.append(f"Lease rows are {len(rows)}; exactly 10 are required")
        row_records = []
        for index, row in enumerate(rows):
            if not isinstance(row, dict):
                reasons.append(f"Lease row {index} is not an object")
                continue
            label = row.get("asset") if isinstance(row.get("asset"), str) and row.get("asset") else f"row {index}"
            before = row.get("usersBefore")
            after = row.get("usersAfter")
            row_restored = row.get("ownershipRestored")
            if not isinstance(row.get("asset"), str) or not row.get("asset"):
                reasons.append(f"Lease row {index} has no asset name")
            if not isinstance(before, int) or isinstance(before, bool) or before < 0:
                reasons.append(f"Lease row {label}: usersBefore is invalid")
            if not isinstance(after, int) or isinstance(after, bool) or after < 0:
                reasons.append(f"Lease row {label}: usersAfter is invalid")
            if isinstance(before, int) and isinstance(after, int) and before != after:
                reasons.append(f"Lease row {label}: usersAfter {after} != usersBefore {before}")
            if row_restored is not True:
                reasons.append(f"Lease row {label}: ownershipRestored is not true")
            row_records.append({"asset": row.get("asset"), "usersBefore": before,
                                "usersAfter": after, "ownershipRestored": row_restored})
        passed = not reasons
        reports.append({"path": relative(path), "sha256": sha256(path),
                        "explicitWarmLeases": explicit, "warmLeasePreparationMs": preparation,
                        "ownershipRestored": restored, "rowCount": len(rows),
                        "rows": row_records, "schemaValidated": passed, "gatePassed": passed,
                        "reasonCount": len(reasons), "reasons": reasons[:20]})
        passing += 1 if passed else 0
    if not reports:
        return {"status": "NOT_YET_MEASURED", "gateSatisfied": False, "reportCount": 0,
                "validatedCount": 0, "reports": [], "explicitWarmLeases": None,
                "warmLeasePreparationMs": None, "ownershipRestored": None}
    sample = reports[0]
    return {"status": "VALIDATED" if passing == len(reports) else "INVALID",
            "gateSatisfied": passing == len(reports) and bool(reports),
            "reportCount": len(reports), "validatedCount": passing, "reports": reports,
            "explicitWarmLeases": sample["explicitWarmLeases"],
            "warmLeasePreparationMs": sample["warmLeasePreparationMs"],
            "ownershipRestored": sample["ownershipRestored"]}


def test_count(value):
    """Counts must be non-negative integers; booleans and fractions are not counts."""
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    if isinstance(value, float):
        if not value.is_integer():
            return None
        value = int(value)
    return value if value >= 0 else None


def test_report_gate(report):
    """Gate one Unity CLI run_tests response on its nested Summary, not the outer success flag."""
    data = report.get("data") if isinstance(report, dict) else None
    result = data.get("result") if isinstance(data, dict) else None
    summary = result.get("Summary") if isinstance(result, dict) else None
    mode = result.get("Mode") if isinstance(result, dict) else None
    row = {"outerSuccess": report.get("success") if isinstance(report, dict) else None,
           "nestedSuccess": result.get("success") if isinstance(result, dict) else None,
           "mode": mode,
           "filterApplied": result.get("FilterApplied") if isinstance(result, dict) else None,
           "durationMs": result.get("Duration") if isinstance(result, dict) else None,
           "counts": {}, "summaryValid": False, "countsConsistent": False,
           "editorMode": isinstance(mode, str) and mode.lower() == "editmode",
           "gatePassed": False, "gateReason": "Missing nested data.result.Summary"}
    if not isinstance(summary, dict):
        return row
    counts = {key: test_count(summary.get(key)) for key in TEST_COUNT_KEYS}
    if any(value is None for value in counts.values()):
        row["gateReason"] = "Summary counts are missing or not non-negative integers"
        return row
    row["counts"], row["summaryValid"] = counts, True
    row["countsConsistent"] = sum(counts[key] for key in TEST_COUNT_KEYS[1:]) == counts["Total"]
    if not row["countsConsistent"]:
        row["gateReason"] = "Summary counts do not add up to Total"
    elif counts["Total"] == 0:
        row["gateReason"] = "Total is 0; a zero-test run is not functional evidence"
    elif not (counts["Passed"] == counts["Total"] and counts["Failed"] == counts["Skipped"] == counts["Inconclusive"] == 0):
        row["gateReason"] = "Not all tests passed (Passed must equal Total and Failed/Skipped/Inconclusive must be 0)"
    elif row["nestedSuccess"] is not True:
        row["gateReason"] = "Nested data.result.success is not true"
    elif not row["editorMode"]:
        row["gateReason"] = "Not declared as an EditMode run"
    else:
        row["gatePassed"] = True
        row["gateReason"] = "Total>0, Passed==Total, Failed/Skipped/Inconclusive==0, nested success true, EditMode"
    return row


def summarize_tests(paths):
    """Only final accepted raw Unity CLI responses belong here; drafts stay in Temp."""
    rows = []
    for path in paths:
        row = test_report_gate(load_json(path))
        row.update({"path": relative(path), "sha256": sha256(path)})
        rows.append(row)
    rows.sort(key=lambda row: row["path"])
    passing = sum(1 for row in rows if row["gatePassed"])
    totals = {key: sum(row["counts"].get(key, 0) for row in rows if row["summaryValid"])
              for key in TEST_COUNT_KEYS}
    if not rows:
        status = "NOT_YET_MEASURED"
    elif passing == len(rows):
        status = "PASSED"
    else:
        status = "IN_PROGRESS"
    return {"status": status, "gateSatisfied": bool(rows) and passing == len(rows),
            "reportCount": len(rows), "passingReportCount": passing,
            "totals": totals, "reports": rows,
            "limitations": [
                "Only nested data.result.Summary counts plus nested data.result.success are evidence; the outer success flag alone is insufficient.",
                "Total must be greater than 0, Passed must equal Total and Failed/Skipped/Inconclusive must be 0.",
                "Only EditMode runs count; a Player/PlayMode run is a different scope.",
                "No failed draft or temp run may be placed in tests/; only final accepted raw CLI responses are read.",
                "Lifecycle OnDestroy callback coverage is invoked reflectively in EditMode; Play-mode automatic destruction dispatch is not covered.",
                "Passing tests prove behavior, not FPS, speedup or map assembly.",
            ]}


def evidence_gate(baseline, matched, tests, optimized, audit, audit_path, lease_audit):
    """SCOPED VALIDATED (CPU only) requires every listed input; missing stays IN PROGRESS."""
    snapshot_total = sum(len(audit[key]) for key in ("sourceFiles", "generatedV04Files", "preservedEvidenceFiles"))
    latest = audit.get("latestComparison") or {}
    profiles = optimized["performanceProfiles"]
    prewarm = optimized.get("prewarmAudit")
    prewarm_rows = prewarm.get("rowCountTotal") if prewarm else None
    matched_reports_ok = (matched["reportCount"] == MATCHED_RUN_COUNT and matched["complete"] is True
                          and matched["compatible"] is True and matched["allReportsPassed"] is True)
    lease_ok = lease_audit.get("gateSatisfied") is True
    matched_ok = matched_reports_ok and lease_ok
    tests_ok = tests["gateSatisfied"] is True
    profiler_ok = profiles.get("gateSatisfied") is True and profiles.get("validatedProfileCount", 0) >= 1
    prewarm_ok = prewarm_rows == 10
    hashes_ok = latest.get("passed") is True and snapshot_total == 48
    totals_text = "/".join(str(tests["totals"][key]) for key in TEST_COUNT_KEYS)
    lease_text = (f"leases={lease_audit.get('explicitWarmLeases')}; "
                  f"ownershipRestored={lease_audit.get('ownershipRestored')}; "
                  f"rows={lease_audit.get('reports')[0]['rowCount'] if lease_audit.get('reports') else 'missing'}")
    checks = [
        {"id": "matchedVerifyAssets",
         "requirement": "Five compatible, passing matched VerifyAssets reports plus a valid 10-lease warm-cache audit with ownership restored",
         "observed": f"{matched['reportCount']}/{MATCHED_RUN_COUNT} present; passed={matched['allReportsPassed']}; compatible={matched['compatible']}; {lease_text}",
         "satisfied": bool(matched_ok), "expected": "optimized-matched/run-0..4.json + matched-cache-lease-audit.json",
         "inputs": [row["path"] for row in ((matched.get("verify") or {}).get("reports") or [])]
                   + [report["path"] for report in lease_audit.get("reports", [])]},
        {"id": "functionalTests",
         "requirement": "At least one real EditMode test report with Total>0, Passed==Total, Failed/Skipped/Inconclusive==0 and nested success true",
         "observed": (f"{tests['reportCount']} report(s); passing={tests['passingReportCount']}; "
                      f"Total/Passed/Failed/Skipped/Inconclusive={totals_text}" if tests["reportCount"] else "0 report(s); no test evidence"),
         "satisfied": bool(tests_ok), "expected": "tests/*.json",
         "inputs": [row["path"] for row in tests["reports"]]},
        {"id": "complementaryProfiler",
         "requirement": "At least one schema-validated terrain-performance-2 profiler report: passed verdict, row quiet/conservation checks and cache/dataOnly/tiny/density collections",
         "observed": f"{profiles['reportCount']} profiler report(s); schema-validated={profiles.get('validatedProfileCount', 0)}",
         "satisfied": bool(profiler_ok), "expected": "optimized/report.json",
         "inputs": [check["path"] for check in profiles.get("schemaChecks", [])]},
        {"id": "prewarmAudit",
         "requirement": "Prewarm audit with exactly 10 asset rows",
         "observed": "absent" if prewarm is None else f"{prewarm_rows} row(s) across {prewarm['reportCount']} report(s)",
         "satisfied": bool(prewarm_ok), "expected": "prewarm-audit.json",
         "inputs": [row["path"] for row in (prewarm["reports"] if prewarm else [])]},
        {"id": "protectedHashes",
         "requirement": "48-file immutable hash snapshot unchanged at the latest comparison",
         "observed": f"{snapshot_total} snapshot files; comparison {'UNCHANGED' if latest.get('passed') else 'FAILED/absent'} ({latest.get('checkedCount', 0)} checked)",
         "satisfied": bool(hashes_ok), "expected": relative(audit_path), "inputs": [relative(audit_path)]},
    ]
    validated = all(check["satisfied"] for check in checks)
    return {"status": "SCOPED_VALIDATED_CPU_ONLY" if validated else "IN_PROGRESS",
            "validated": validated,
            "scope": "Unity Editor edit mode CPU milliseconds; not FPS, not a Player build, not map assembly or gameplay approval.",
            "checks": checks,
            "unsatisfied": [check["id"] for check in checks if not check["satisfied"]]}


def record_identity(record):
    primary = next((f"{k}={record[k]}" for k in IDENTITY_KEYS
                    if isinstance(record.get(k), (str, int)) and not isinstance(record[k], bool)
                    and str(record[k])), "")
    context = [f"{k}={record[k]}" for k in CONTEXT_KEYS
               if isinstance(record.get(k), str) and record[k] and f"{k}={record[k]}" != primary]
    # A phase alone is useful when a sample list separates first hit from sustained hits.
    return ";".join(([primary] if primary else []) + context)


def numeric_leaves(value, path="", leaf="", unit_hint=None):
    """Yield full numeric paths, replacing named probe indices with stable identities.

    Unnamed sample/run/hit lists pool samples. Other unnamed record lists keep their
    index to avoid silently combining unlike probes. Explicit phase/cache fields keep
    first-hit, sustained, prewarm and initialization measurements separate.
    """
    if isinstance(value, dict):
        unit = value.get("unit", value.get("units", unit_hint))
        for key, child in value.items():
            yield from numeric_leaves(child, f"{path}.{key}" if path else key, key, unit)
    elif isinstance(value, list):
        occurrences = defaultdict(int)
        for index, child in enumerate(value):
            if numeric(child):
                yield path, leaf, unit_hint, child
            elif isinstance(child, dict):
                identity = record_identity(child)
                suffix = identity or ("*" if leaf.lower() in SAMPLE_LISTS else str(index))
                if leaf.lower() == "operations" and identity:
                    # The first flush may mount all cold chunks. Never silently fold
                    # it into the 15 later sustained flushes of that same body.
                    suffix += f";occurrence={occurrences[identity]}"
                    occurrences[identity] += 1
                yield from numeric_leaves(child, f"{path}[{suffix}]", leaf, unit_hint)
            elif isinstance(child, list):
                yield from numeric_leaves(child, f"{path}[{index}]", leaf, unit_hint)
    elif numeric(value):
        yield path, leaf, unit_hint, value


def timing_field(name, unit):
    return bool(re.search(r"(?:Ms|_ms|Milliseconds|_milliseconds)$", name) or
                str(unit).lower() in {"ms", "millisecond", "milliseconds"})


def is_allocation_field(name):
    """Allocation bytes and GC-generation counts are kept only when positively validated."""
    name = str(name)
    return bool(re.search(r"allocat", name, re.I) or re.fullmatch(r"gen[012]", name, re.I))


def allocation_state(report):
    """(available, reason) from report.allocation, or guarded legacy prewarm fields."""
    if not isinstance(report, dict):
        return None, "report is not an object"
    namespace = report.get("allocation")
    if isinstance(namespace, dict):
        method_available = as_bool(namespace.get("methodAvailable"))
        declared = as_bool(namespace.get("supported"))
        positive = as_int(namespace.get("positiveControlBytes"))
        observed = as_int(namespace.get("maxDelta"))
        if observed is None:
            observed = as_int(namespace.get("firstDelta"))
        prefix = "report.allocation"
        if method_available is False:
            return False, f"{prefix}.methodAvailable=false"
    else:
        declared = as_bool(report.get("allocationCounterAvailable"))
        positive = as_int(report.get("allocationPositiveControlBytes"))
        observed = as_int(report.get("allocationObservedDeltaBytes"))
        prefix = "report"
    if declared is False:
        return False, f"{prefix}.supported=false"
    if declared is True:
        if positive is not None and observed is not None and positive > 0 and observed >= positive:
            return True, f"{prefix}.supported=true and the positive control observed the allocation"
        return False, f"{prefix}.supported=true but its positive control did not observe the allocation"
    if positive is not None and observed is not None:
        if positive > 0 and observed >= positive:
            return True, f"{prefix} positive control observed the expected allocation"
        return False, f"{prefix} positive control did not observe the expected allocation"
    return None, "no allocation-counter availability marker"


def group_allocation_state(reports):
    states = [allocation_state(report) for report in reports]
    for state, reason in states:
        if state is False:
            return False, reason
    if states and all(state is True for state, _ in states):
        return True, "all reports positively validate the allocation counter"
    return None, "allocation counter is not positively validated"


def combine_allocation_state(global_state, group_state):
    if global_state is False or group_state is False:
        return False
    if group_state is True:
        return True
    if global_state is True:
        return True
    return None


def effective_allocation_reason(audit, group_state, group_reason):
    if group_state is False:
        return group_reason
    if audit.get("available") is False:
        return audit.get("reason", group_reason)
    if group_state is True:
        return group_reason
    return audit.get("reason") or group_reason


ALLOCATION_KEY_GROUPS = {
    "available": ("allocationcounteravailable", "allocationavailable", "counteravailable", "supported"),
    "positive": ("allocationpositivecontrolbytes", "positivecontrolbytes", "controlbytes", "requestedbytes"),
    "observed": ("allocationobserveddeltabytes", "observeddeltabytes", "threaddeltabytes", "threaddelta", "deltabytes"),
    "heap": ("monousedheapdeltabytes", "heapuseddeltabytes", "heapuseddelta", "monousedheapdelta"),
}


def flatten_report(value, depth=0, output=None, limit=6):
    if output is None:
        output = {}
    if depth > limit:
        return output
    if isinstance(value, dict):
        for key, child in value.items():
            if isinstance(child, (dict, list)):
                flatten_report(child, depth + 1, output, limit)
            else:
                output.setdefault(str(key).strip().lower(), child)
    elif isinstance(value, list):
        for child in value[:20]:
            flatten_report(child, depth + 1, output, limit)
    return output


def as_int(value):
    if isinstance(value, bool):
        return None
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def as_bool(value):
    if isinstance(value, bool):
        return value
    if isinstance(value, str):
        if value.strip().lower() in ("true", "1", "yes"):
            return True
        if value.strip().lower() in ("false", "0", "no"):
            return False
    return None


def parse_allocation_audit(report):
    """Read a curated audit or a Unity run_script positive-control response."""
    flat = flatten_report(report)
    fields = {}
    for key, names in ALLOCATION_KEY_GROUPS.items():
        for name in names:
            if name in flat and not isinstance(flat[name], (dict, list)):
                fields[key] = flat[name]
                break
    text = " ".join(str(value) for value in flat.values() if isinstance(value, str))
    for key, pattern in (("positive", r"positiveControlBytes\s*[=:]\s*(\d+)"),
                         ("observed", r"threadDelta\s*[=:]\s*(-?\d+)"),
                         ("heap", r"heapUsedDelta\s*[=:]\s*(-?\d+)"),
                         ("available", r"supported\s*[=:]\s*(true|false)")):
        if key in fields:
            continue
        match = re.search(pattern, text, re.I)
        if match:
            fields[key] = match.group(1)
    declared, positive = as_bool(fields.get("available")), as_int(fields.get("positive"))
    observed, heap = as_int(fields.get("observed")), as_int(fields.get("heap"))
    if declared is False:
        state, reason = False, "positive control reports the allocation counter unsupported"
    elif declared is True:
        if positive is not None and observed is not None and positive > 0 and observed >= positive:
            state, reason = True, "positive control allocated the expected bytes"
        else:
            state, reason = False, "positive control declares support but did not observe the allocation"
    elif positive is not None and observed is not None:
        if positive > 0 and observed >= positive:
            state, reason = True, "positive control allocated the expected bytes"
        else:
            state, reason = False, "positive control did not observe the expected allocation"
    else:
        state, reason = None, "no usable positive-control result"
    return {"state": "available" if state is True else ("unavailable" if state is False else "unknown"),
            "available": state, "positiveControlBytes": positive,
            "observedDeltaBytes": observed, "monoUsedHeapDeltaBytes": heap, "reason": reason}


def summarize_allocation_audit(paths):
    entries = []
    for path in paths:
        entry = {"path": relative(path), "sha256": sha256(path)}
        entry.update(parse_allocation_audit(load_json(path)))
        entries.append(entry)
    if not entries:
        return {"state": "unknown", "available": None, "reason": "no allocation-counter audit supplied",
                "positiveControlBytes": None, "observedDeltaBytes": None,
                "monoUsedHeapDeltaBytes": None, "entries": []}
    if any(entry["available"] is False for entry in entries):
        available = False
        reason = "at least one positive-control audit reports the allocation counter unsupported"
    elif all(entry["available"] is True for entry in entries):
        available = True
        reason = "all supplied positive-control audits observed the expected allocation"
    else:
        available = None
        reason = "allocation-counter audit is incomplete or unvalidated"
    sample = next((entry for entry in entries if entry["positiveControlBytes"] is not None), entries[0])
    return {"state": "available" if available is True else ("unavailable" if available is False else "unknown"),
            "available": available, "reason": reason,
            "positiveControlBytes": sample["positiveControlBytes"],
            "observedDeltaBytes": sample["observedDeltaBytes"],
            "monoUsedHeapDeltaBytes": sample["monoUsedHeapDeltaBytes"],
            "entries": entries}


def summarize_flexible(paths, allocation_available=None, allocation_reason=""):
    grouped, units, statuses = defaultdict(list), {}, []
    suppressed, sentinels = set(), set()
    for path in paths:
        report = load_json(path)
        for metric, leaf, unit, value in numeric_leaves(report):
            if metric == "allocation" or metric.startswith("allocation.") or ".allocation." in metric:
                suppressed.add(metric)
                continue
            if is_allocation_field(leaf):
                if allocation_available is not True:
                    suppressed.add(metric)
                    continue
                if value < 0:
                    sentinels.add(metric)
                    continue
            grouped[metric].append(value)
            units[metric] = "ms" if timing_field(leaf, unit) else "raw (see report field)"
        statuses.append({"path": relative(path), "sha256": sha256(path),
                         "declaredStatus": {k: report[k] for k in ("version", "passed", "status", "failures", "unsupported") if isinstance(report, dict) and k in report}})
    evidence = {"state": "available" if allocation_available is True else ("unavailable" if allocation_available is False else "unknown"),
                "reason": allocation_reason,
                "suppressedFieldCount": len(suppressed), "suppressedFields": sorted(suppressed)[:50],
                "sentinelFieldCount": len(sentinels), "sentinelFields": sorted(sentinels)[:50]}
    return {"status": "REPORTS_DISCOVERED_REQUIRES_REVIEW" if paths else "NOT_YET_MEASURED",
            "reportCount": len(paths), "reports": statuses,
            "timingsMs": {k: stats(v) for k, v in sorted(grouped.items()) if units[k] == "ms"},
            "otherNumericFields": {k: stats(v) for k, v in sorted(grouped.items()) if units[k] != "ms"},
            "allocationEvidence": evidence,
            "limits": ["Different metric paths, phase/cache labels and named probes are never merged.",
                       "Repeated operation stages retain occurrence=0,1,... so a first cold chunk mount is not pooled into subsequent sustained hits.",
                       "An n value counts numeric observations, not necessarily independent runs; array samples may be within-run.",
                       "Already aggregated median/min/max fields remain distinct fields; they are not reconstructed raw samples.",
                       "Unknown numeric fields keep raw units; only *Ms/*_ms/*Milliseconds or explicit millisecond unit fields are timings.",
                       "Allocation/GC metrics are suppressed unless a positive control proves the counter works; -1 sentinels are never reported as medians.",
                       "No automatic cross-schema speedup, target budget, frame-time or FPS claim."]}


def is_prewarm_report(path, report):
    """TerrainPrewarmReport: a private-clone template/chunk audit, not a body probe."""
    if path.name.startswith("prewarm-audit") and path.suffix.lower() == ".json":
        return True
    return (isinstance(report, dict) and "version" not in report and "measurement" in report
            and isinstance(report.get("rows"), list))


def profile_schema_check(path, report):
    """Validate the real terrain-performance-2 report/row/collection contract from Performance.cs.

    Legacy terrain-performance-1 is recognized and summarized but not gate-validated. No metric
    is invented and measured stages keep their own operation stage path.
    """
    result = {"path": relative(path), "sha256": sha256(path),
              "version": report.get("version") if isinstance(report, dict) else None,
              "recognized": False, "schemaValidated": False, "gatePassed": False,
              "passed": None, "verdict": None, "rowCount": 0, "summaryCount": 0,
              "cacheProbeCount": 0, "prewarmRowCount": 0, "prewarmProbeCells": None,
              "allocationCounterAvailable": None, "allocationMethodAvailable": None,
              "allocationPositiveControlBytes": None, "allocationObservedDeltaBytes": None,
              "allocationHeapDeltaBytes": None, "allocationNote": None,
              "reasonCount": 0, "reasons": []}
    if not isinstance(report, dict) or result["version"] not in PROFILE_VERSIONS:
        result["reasonCount"], result["reasons"] = 1, ["Not a recognized terrain-performance report"]
        return result
    result["recognized"] = True
    result["passed"], result["verdict"] = report.get("passed"), report.get("verdict")
    namespace = report.get("allocation") if isinstance(report.get("allocation"), dict) else {}
    declared = namespace.get("supported") if "supported" in namespace else report.get("allocationCounterAvailable")
    positive = namespace.get("positiveControlBytes", report.get("allocationPositiveControlBytes"))
    observed = namespace.get("maxDelta", namespace.get("firstDelta", report.get("allocationObservedDeltaBytes")))
    heap_before, heap_after = as_int(namespace.get("heapBefore")), as_int(namespace.get("heapAfter"))
    result.update({"allocationCounterAvailable": as_bool(declared),
                   "allocationMethodAvailable": as_bool(namespace.get("methodAvailable")),
                   "allocationPositiveControlBytes": as_int(positive),
                   "allocationObservedDeltaBytes": as_int(observed),
                   "allocationHeapDeltaBytes": (heap_after - heap_before
                                                if heap_before is not None and heap_after is not None else None),
                   "allocationNote": namespace.get("note")})
    if result["version"] != "terrain-performance-2":
        result["reasonCount"] = 1
        result["reasons"] = ["Legacy terrain-performance-1 is recognized but not schema-validated; terrain-performance-2 is required for this gate"]
        return result
    reasons = []

    def need(condition, message):
        if not condition:
            reasons.append(message)

    rows = report.get("rows") if isinstance(report.get("rows"), list) else []
    summaries = report.get("summaries") if isinstance(report.get("summaries"), list) else []
    cache = report.get("cache") if isinstance(report.get("cache"), list) else []
    prewarm_rows = report.get("prewarm") if isinstance(report.get("prewarm"), list) else []
    files = report.get("files") if isinstance(report.get("files"), list) else []
    probe = report.get("prewarmProbe")
    result.update({"rowCount": len(rows), "summaryCount": len(summaries),
                   "cacheProbeCount": len(cache), "prewarmRowCount": len(prewarm_rows),
                   "prewarmProbeCells": probe.get("cells") if isinstance(probe, dict) else None})
    need(report.get("passed") is True, "Top-level passed is not true")
    verdict = report.get("verdict")
    need(not isinstance(verdict, str) or verdict.startswith("FUNCTIONAL CHECKS PASSED"),
         "Top-level verdict is not FUNCTIONAL CHECKS PASSED")
    failures = report.get("failures")
    need(isinstance(failures, list) and not failures, "Top-level failures are not empty")
    for key in ("sceneRestored", "globalPhysicsUntouched", "templateCachePreserved", "templateCacheRestored"):
        need(report.get(key) is True, f"{key} is not true")
    contract = report.get("contract")
    need(isinstance(contract, dict) and contract.get("matchesContract") is True, "Runtime contract does not match")
    source = report.get("source")
    need(isinstance(source, dict) and source.get("integrityPassed") is True, "Source integrity did not pass")
    need(bool(files), "Source file audit is empty")
    need(bool(files) and all(isinstance(entry, dict) and entry.get("unchanged") is True for entry in files),
         "A source file audit entry is changed or malformed")
    need(bool(rows), "Measured rows are empty")
    need(bool(summaries), "Measured-stage summaries are empty")
    need(bool(cache), "Cache probes are empty")
    need(bool(prewarm_rows), "Prewarm rows are empty")
    for row in rows:
        if not isinstance(row, dict):
            need(False, "A measured row is not an object")
            continue
        label = f"{row.get('asset')}/{row.get('scenario')}"
        for key, expected in (("conserved", True), ("uniquePhysicalOwners", True),
                              ("physicsBodiesMatchRuntimes", True), ("runtimeEnabledAfter", False)):
            if key in row:
                need(row.get(key) is expected, f"Row {label}: {key} is {row.get(key)}")
        if "passed" in row:
            need(row.get("passed") is True, f"Row {label}: row passed is not true")
        if row.get("chunkSupported") is True:
            need(row.get("intactSingleRootRenderer") is True, f"Row {label}: intactSingleRootRenderer is not true")
            if row.get("scenario") in ("single-lethal", "second-local-lethal"):
                need(row.get("firstHitIncludesRendererActivation") is True,
                     f"Row {label}: first-hit renderer activation is not included")
        if row.get("scenario") in ("idle-1024", "nonlethal-expiry"):
            need(row.get("noRebuild") is True, f"Row {label}: noRebuild is not true")
        if row.get("scenario") == "nonlethal-expiry":
            need(row.get("healthRestored") is True, f"Row {label}: healthRestored is not true")
        operations = row.get("operations")
        need(isinstance(operations, list) and bool(operations), f"Row {label}: no measured operations")
    for entry in cache:
        if not isinstance(entry, dict):
            need(False, "A cache probe is not an object")
            continue
        for key in ("sharedTemplates", "aggregateShared", "intactSingleRootRenderer",
                    "chunksActivateOnFirstLethal", "peerIntactAfterHit"):
            need(entry.get(key) is True, f"Cache probe {entry.get('asset')}: {key} is not true")
    if isinstance(probe, dict):
        need(isinstance(probe.get("cells"), int) and probe.get("cells") > 0, "Prewarm probe cell count is not positive")
        need(isinstance(probe.get("chunks"), int) and probe.get("chunks") > 0, "Prewarm probe chunk count is not positive")
        for key in ("reusedOnSecondInstance", "intactSingleRootRenderer", "noChunkObjectsBeforeHit",
                    "chunksActivateOnFirstLethal", "chunkCountMatchesMountedMeshes", "rootAggregateHidden",
                    "peerStayedIntact", "prewarmedResourcesReused", "cacheRestored"):
            need(probe.get(key) is True, f"Prewarm probe: {key} is not true")
    else:
        need(False, "Prewarm probe is missing")
    data_only = report.get("dataOnly")
    if isinstance(data_only, dict):
        for key in ("dataOnly", "renderCacheAbsent", "renderMeshesEmpty", "noTemplate", "noChunks",
                    "noMountedMesh", "rendererDisabled", "noColliders", "noRigidbody",
                    "cacheStillAbsentAfterDamage"):
            need(data_only.get(key) is True, f"dataOnly: {key} is not true")
        need(data_only.get("removed") == 1, "dataOnly probe did not remove exactly one cell")
    else:
        need(False, "dataOnly probe is missing")
    tiny = report.get("tiny")
    if isinstance(tiny, dict):
        for key in ("apiPresent", "defaultOff", "defaultPhysical", "optInStored",
                    "retainedLargePhysical", "extentAboveBoundaryPhysical", "conserved"):
            need(tiny.get(key) is True, f"Tiny probe: {key} is not true")
    else:
        need(False, "Tiny probe is missing")
    density = report.get("density")
    if isinstance(density, dict):
        need(isinstance(density.get("instances"), int) and density["instances"] > 0, "Density probe is empty")
        need(isinstance(density.get("render"), dict), "Density render metrics are missing")
    else:
        need(False, "Density probe is missing")
    result["reasonCount"] = len(reasons)
    result["reasons"] = reasons[:20]
    result["schemaValidated"] = not reasons
    result["gatePassed"] = not reasons
    return result


def summarize_optimized(paths, allocation_audit=None):
    """Never pool matched verification, sustained-attack profiles, prewarm audits or unknown layouts."""
    allocation_audit = allocation_audit or {"available": None, "reason": "no allocation-counter audit supplied"}
    global_allocation, global_reason = allocation_audit.get("available"), allocation_audit.get("reason", "")
    verify, profiles, prewarm, other = [], [], [], defaultdict(list)
    loaded = {path: load_json(path) for path in paths}
    profile_rows = set()
    for path, report in loaded.items():
        if isinstance(report, dict) and report.get("version") in PROFILE_VERSIONS:
            profiles.append(path)
            profile_rows.update(json.dumps(row, sort_keys=True) for row in report.get("rows", []))
    duplicates, prewarm_rows = [], []
    for path, report in loaded.items():
        if path in profiles:
            continue
        if isinstance(report, dict) and report.get("version") == "mining-round2-verification/1":
            verify.append(path)
        elif is_prewarm_report(path, report):
            prewarm.append(path)
            rows = report.get("rows") if isinstance(report, dict) else None
            prewarm_rows.append({"path": relative(path), "sha256": sha256(path),
                                 "rows": len(rows) if isinstance(rows, list) else 0})
        elif isinstance(report, list) and report and all(json.dumps(row, sort_keys=True) in profile_rows for row in report):
            duplicates.append(relative(path))
        else:
            version = str(report.get("version", "unknown-object")) if isinstance(report, dict) else "unknown-array"
            other[version].append(path)
    profile_group_state, profile_group_reason = group_allocation_state([loaded[path] for path in profiles])
    profile_allocation = combine_allocation_state(global_allocation, profile_group_state)
    profile_reason = effective_allocation_reason(allocation_audit, profile_group_state, profile_group_reason)
    prewarm_group_state, prewarm_group_reason = group_allocation_state([loaded[path] for path in prewarm])
    prewarm_allocation = combine_allocation_state(global_allocation, prewarm_group_state)
    prewarm_reason = effective_allocation_reason(allocation_audit, prewarm_group_state, prewarm_group_reason)
    prewarm_summary = summarize_flexible(prewarm, prewarm_allocation, prewarm_reason) if prewarm else None
    if prewarm_summary is not None:
        prewarm_summary["rowCounts"] = prewarm_rows
        prewarm_summary["rowCountTotal"] = sum(row["rows"] for row in prewarm_rows)
    profile_summary = summarize_flexible(profiles, profile_allocation, profile_reason)
    profile_checks = [profile_schema_check(path, loaded[path]) for path in profiles]
    profile_summary["schemaChecks"] = profile_checks
    profile_summary["validatedProfileCount"] = sum(1 for check in profile_checks if check["schemaValidated"])
    profile_summary["gateSatisfied"] = bool(profile_checks) and all(check["gatePassed"] for check in profile_checks)
    if profile_checks:
        profile_summary["status"] = ("SCHEMA_VALIDATED" if profile_summary["gateSatisfied"]
                                     else "REPORTS_DISCOVERED_REQUIRES_REVIEW")
    other_allocation = combine_allocation_state(global_allocation, None)
    return {"status": "REPORTS_DISCOVERED_REQUIRES_REVIEW" if paths else "NOT_YET_MEASURED",
            "reportCount": len(paths) - len(duplicates),
            "sameProbeVerifyAssets": summarize_baseline(verify) if verify else None,
            "performanceProfiles": profile_summary,
            "prewarmAudit": prewarm_summary,
            "otherLayouts": {name: summarize_flexible(files, other_allocation, global_reason)
                             for name, files in sorted(other.items())},
            "duplicateRowFilesExcluded": duplicates,
            "comparisonLimits": [
                "The five matched optimized VerifyAssets runs are summarized in their own group; they are not pooled with these complementary reports.",
                "Performance profiles are separate synchronous operation probes, including sequential sustained attacks, not repetitions of VerifyAssets.",
                "terrain-performance-2 reports are validated against their actual passed/verdict, row quiet/conservation and collection checks; legacy terrain-performance-1 is recognized but does not satisfy the gate.",
                "Measured operation stages keep their own stage path and are never pooled into a frame budget or one silent total.",
                "Prewarm audits clone each source into a fresh private cache key and create no body instance; they measure template/chunk/collision construction, not startup FPS or a cold VerifyAssets baseline.",
                "Unity-reported aggregate mesh bytes and chunk-stream lower-bound estimates are different native measurements and are never added into one exact total or into damage ms.",
                "First-hit cold chunk mounting must stay separate from prewarm/initialization and later sustained hits; warm-only results cannot establish overall success.",
                "Full profile reports take priority over byte-equivalent row exports, preventing duplicate samples.",
                "No automatic speedup or full-station real-time acceptance is inferred."]}



def compare_audit(path):
    """Keep the initial snapshot immutable; refresh only latestComparison and status."""
    audit = load_json(path)
    if audit.get("schemaVersion") != "terrain-source-hash-audit/1":
        raise ValueError("Unrecognized audit schema; refusing to reset the snapshot")
    required = ("snapshotUtc", "referenceManifest", "sourceFiles", "generatedV04Root",
                "generatedV04Files", "preservedEvidenceFiles")
    incomplete = [key for key in required if key not in audit]
    if incomplete:
        raise ValueError(f"Audit snapshot lacks {', '.join(incomplete)}; refusing to write a comparison")
    if not audit["sourceFiles"] or not audit["generatedV04Files"]:
        raise ValueError("Audit snapshot is empty; refusing to replace it with a comparison")
    for entry in audit["sourceFiles"]:
        if not all(key in entry for key in ("path", "referenceSha256", "initialSha256", "matchesReferenceAtSnapshot")):
            raise ValueError("Audit source snapshot entry is incomplete; refusing to rewrite it")
    changed, missing, checked = [], [], []
    source_ok = all(e["matchesReferenceAtSnapshot"] for e in audit["sourceFiles"])
    for section in ("sourceFiles", "generatedV04Files", "preservedEvidenceFiles"):
        for entry in audit[section]:
            target = resolve(entry["path"])
            expected = entry["referenceSha256"] if section == "sourceFiles" else entry["sha256"]
            current = sha256(target) if target.is_file() else None
            row = {"path": entry["path"], "expectedSha256": expected, "currentSha256": current,
                   "unchanged": current == expected}
            checked.append(row)
            if current is None:
                missing.append(entry["path"])
            elif current != expected:
                changed.append(entry["path"])
    generated_root = resolve(audit["generatedV04Root"])
    actual = {relative(p) for p in generated_root.rglob("*") if p.is_file()}
    if generated_root.with_suffix(".meta").is_file():
        actual.add(relative(generated_root.with_suffix(".meta")))
    expected_set = {entry["path"] for entry in audit["generatedV04Files"]}
    added = sorted(actual - expected_set)
    passed = source_ok and not changed and not missing and not added
    audit["status"] = "UNCHANGED_AT_LATEST_COMPARISON" if passed else "INTEGRITY_FAILURE"
    audit["latestComparison"] = {"checkedUtc": utc_now(), "passed": passed,
                                  "checkedCount": len(checked), "changed": changed,
                                  "missing": missing, "generatedV04Added": added, "files": checked}
    return audit


def display_stat(group, metric):
    s = group["metrics"].get(metric)
    return f"{s['median']:.3f} ({s['min']:.3f}–{s['max']:.3f})" if s else "not measured"


def stable_value(group, metric):
    s = group["metrics"].get(metric)
    if not s:
        return "not measured"
    return f"{s['min']:,}" if s["min"] == s["max"] else f"{s['min']:,}–{s['max']:,}"


def fmt_ms(value, digits=3):
    return "not measured" if value is None else f"{value:,.{digits}f}"


def fmt_delta(value, digits=3):
    if value is None:
        return "—"
    if abs(value) < 0.5 * 10 ** (-digits):
        return f"{0:.{digits}f}"
    return f"{value:+,.{digits}f}"


def compare_row(comparison, phase, identity, metric):
    for row in (comparison or {}).get("deltas", []):
        if row["phase"] == phase and row["identity"] == identity and row["metric"] == metric:
            return row
    return None


def pair_text(row, digits=3):
    if not row:
        return "not measured"
    return (f"{fmt_ms(row['baselineMedian'], digits)} → {fmt_ms(row['matchedMedian'], digits)} "
            f"({fmt_delta(row['delta'], digits)})")


def short(value, limit=70):
    text = json.dumps(value, ensure_ascii=False) if isinstance(value, (list, dict)) else str(value)
    return text if len(text) <= limit else text[:limit - 1] + "…"


def readme_text(summary, audit, summary_path, audit_path):
    b = summary["baseline"]
    matched = summary["matchedVerifyAssets"]
    verify = matched.get("verify")
    comparison = matched.get("comparison") or {}
    ready = comparison.get("status") == "DESCRIPTIVE_ONLY"
    tests = summary["tests"]
    gate = summary["gate"]
    allocation = summary.get("allocationAudit") or {}
    lease = summary.get("matchedLeaseAudit") or {}
    profile_allocation = (summary["optimized"]["performanceProfiles"].get("allocationEvidence") or {})
    prewarm_allocation = ((summary["optimized"].get("prewarmAudit") or {}).get("allocationEvidence") or {})
    validated = gate["validated"] is True

    def fmt_bytes(value):
        return f"{value:,}" if isinstance(value, int) else "not recorded"
    gate_names = {"matchedVerifyAssets": "matched VerifyAssets",
                  "functionalTests": "functional tests",
                  "complementaryProfiler": "complementary profiler",
                  "prewarmAudit": "prewarm audit",
                  "protectedHashes": "protected hashes"}
    pending_names = [gate_names.get(name, name) for name in gate["unsatisfied"]]
    env = b["environment"]["labels"]
    snapshot_total = sum(len(audit[key]) for key in ("sourceFiles", "generatedV04Files", "preservedEvidenceFiles"))
    if not matched["reportCount"]:
        matched_state = ("The five directly matched optimized VerifyAssets runs (`optimized-matched/run-0..4.json`) are **not present yet**; "
                         "no matched numbers, deltas, cold baseline or FPS are inferred.")
    elif not matched["complete"] and matched["reportCount"] > MATCHED_RUN_COUNT:
        matched_state = (f"More than five matched reports are present ({matched['reportCount']}); exactly five are required, "
                         "so no comparison is made.")
    elif not matched["complete"]:
        matched_state = (f"Matched optimized reports are **partial** ({matched['reportCount']} of {MATCHED_RUN_COUNT} present); "
                         "no comparison is made from an incomplete set.")
    elif not matched["compatible"]:
        matched_state = ("All five matched reports are present, but their probe identities differ from the baseline; no comparison is made.")
    elif not matched["allReportsPassed"]:
        matched_state = "All five matched reports are present, but at least one failed; these numbers are not benefit evidence."
    else:
        matched_state = ("All five matched reports are present and passed. The deltas below are descriptive CPU-ms differences for review, "
                         "not a verified speedup; the evidence gate below decides the final label.")
    heading = ("# Terrain performance — SCOPED VALIDATED (CPU only)" if validated
               else "# Terrain performance — IN PROGRESS")
    if validated:
        status = ("**Status: SCOPED VALIDATED (CPU only).** All required inputs are present and passing: five compatible matched "
                  "VerifyAssets runs, at least one real EditMode test report with Total>0 and no failures/skips/inconclusives, the "
                  "schema-validated terrain-performance-2 profiler report, the 10-row prewarm audit and the unchanged 48-file hash snapshot. This label covers "
                  "only that Editor CPU evidence: it is not FPS, Player-build, real-time, gameplay or map-assembly approval.")
    else:
        status = (f"**Status: IN PROGRESS.** {matched_state} Pending evidence gates: {', '.join(pending_names)}. "
                  "No target speed, FPS, Player-build, real-time, memory saving or map-assembly approval is claimed.")
    lines = [heading, "", status, "",
             "## Scope and preserved evidence", "",
             "Optimize terrain before any more map assembly. This report does not assemble a map or modify source art, saved assets, ship/AI/flight behavior, gameplay tuning or project history.", "",
             f"The original v04 {link('Documentation/MiningStation-Round2-2026-11-01/v04/README.md', 'README')} and evidence remain preserved; this is a separate fresh baseline, not a replacement for those earlier measurements.", "",
             f"Machine-readable results: {link(summary_path, 'summary')} · {link(audit_path, 'source/asset hash audit')}. Analyzer: {link('tools/TerrainPerformance/Summarize.py', 'Summarize.py')}.", "",
             "## Optimized-runtime design under measurement", "",
             "The baseline tables below are the unchanged v04 probe. These are the optimized-runtime design points the matched runs are meant to measure, not proof that a benefit already exists:", "",
             "- **Chunk rendering is lazy.** An intact asset keeps its single root renderer and aggregate mesh; exact-4-cube (`exact4cube`) chunk render meshes activate only on the **first lethal hit** that actually destroys or detaches geometry, not on load, validation or warm initialization. The first hit's `flushMs`/`damageMs` therefore carries renderer activation and draw-call setup, kept in the hit total instead of being moved into setup or hidden in `applyDamageMs`.",
             "- **Collision work is subtracted, reused and bounded.** Collision shapes are subtracted from and reused for the affected cells rather than rebuilding every collider; recompaction of the remaining cells is bounded per hit so one hit cannot trigger an unbounded whole-asset rebuild.",
             "- **Connectivity is certified locally, with a full fallback.** A local connectivity certificate decides island/fragment status for the damaged neighborhood; boundary or ambiguous cases fall back to the full connectivity oracle instead of guessing, so bodies and fragments are neither fabricated nor missed.",
             "- **HP expiry is sparse and quiet.** Damaged cells whose HP lapses expire sparsely without respawning geometry or rebuilding chunk meshes; expiry is separate from lethal-hit destruction and does not reset the asset.",
             "- **Tiny-fragment rendering is OFF by default.** The optional render-only policy (`maxCells = 0`) applies only to **newly detached** fragments whose **occupied-cell count ≤ 4 AND full occupied world-bounds maximum dimension ≤ 2 world units**. Both conditions are required (not an OR, not a chunk-size approximation); intact or pre-existing geometry is unaffected and there is no age- or distance-based removal.", "",
             "## Measurement groups kept separate", "",
             "The analyzer keeps the optimized-runtime and evidence inputs separate and never pools them:", "",
             "| Group | Default path | What it is |", "|---|---|---|",
             f"| Matched VerifyAssets | `optimized-matched/run-0..4.json` | Five same-probe warm-cache runs of the same {link('tools/MiningStationRound2/Verification.cs', 'VerifyAssets')} call as the baseline, written by {link('tools/TerrainPerformance/OptimizedMatch.cs', 'OptimizedMatch.cs')}. The only group compared with the baseline. |",
             "| Complementary profiler | `optimized/report.json` | terrain-performance-2 measured-stage rows plus row/collection checks and the synthetic prewarm probe; legacy terrain-performance-1 is recognized but not gate-validated. Not a repetition of VerifyAssets. |",
             "| Prewarm audit | `prewarm-audit.json` | Private-cache template/chunk/collision construction with no body instance; not a startup or FPS probe. |",
             "| Allocation counter audit | `allocation-counter-audit.json` | Positive-control proof that the Editor allocation counter is unavailable; not a performance metric and never a CPU-scope blocker. |",
             "| Matched warm-cache lease audit | `matched-cache-lease-audit.json` | Ten explicit source-template leases and restored user ownership; preparation/cleanup timing context, not a performance feature. |",
             "| Functional tests | `tests/*.json` | Final accepted raw Unity CLI `run_tests` Editor responses; only the nested Summary counts and nested success are gated. |", "",
             "Matched inputs may also be passed with `--matched`. A matched file also selected by `--optimized` is removed from that group and recorded in the summary, so no sample is counted twice. Complementary reports, when present, stay under `optimized.performanceProfiles`, `optimized.prewarmAudit` and `optimized.otherLayouts` in the summary and are never compared with the baseline.", ""]
    lines += ["## Evidence gate — final status", "",
              "The final label is **SCOPED VALIDATED (CPU only)** only when every row below is met. It is never a map-assembly, gameplay, Player-build, real-time or FPS approval. Missing inputs stay absent rather than guessed.", "",
              "| Gate | Input(s) | Observed | State |", "|---|---|---|---|"]
    for check in gate["checks"]:
        inputs = check["inputs"]
        if not inputs:
            input_text = f"`{check['expected']}`"
        elif len(inputs) > 3:
            input_text = " · ".join(link(path) for path in inputs[:2]) + f" … ({len(inputs)} files)"
        else:
            input_text = " · ".join(link(path) for path in inputs)
        lines.append(f"| {gate_names.get(check['id'], check['id'])} | {input_text} | {check['observed']} | {'met' if check['satisfied'] else 'pending'} |")
    lines += ["", "**Scope: Editor only.** Every gate above is Unity Editor edit-mode evidence in CPU milliseconds; none of it is a Player build, frame time, FPS, speedup, gameplay or map-assembly approval.", ""]
    lines += ["## Functional tests — nested CLI gate", ""]
    if not tests["reportCount"]:
        lines += ["Expected final inputs: `Documentation/TerrainPerformance-2026-11-01/tests/*.json` (only final accepted raw Unity CLI `run_tests` responses). Not present yet, so no test counts, totals or pass claim is inferred.", ""]
    else:
        lines += ["| Report | Mode | Total | Passed | Failed | Skipped | Inconclusive | Nested success | Gate | SHA-256 (first 12) |",
                  "|---|---:|---:|---:|---:|---:|---:|---|---|---|"]
        for row in tests["reports"]:
            counts = row["counts"]
            nested = "true" if row["nestedSuccess"] is True else ("false" if row["nestedSuccess"] is False else "missing")
            mode = row["mode"] if isinstance(row["mode"], str) else "missing"
            lines.append(f"| {link(row['path'])} | {mode} | {counts.get('Total', '—')} | {counts.get('Passed', '—')} | "
                         f"{counts.get('Failed', '—')} | {counts.get('Skipped', '—')} | {counts.get('Inconclusive', '—')} | {nested} | "
                         f"{'pass' if row['gatePassed'] else 'fail'} | `{row['sha256'][:12]}` |")
        lines += ["", f"Test gate: **{tests['status']}**; {tests['passingReportCount']} of {tests['reportCount']} report(s) pass. "
                      f"Totals across valid summaries: Total {tests['totals']['Total']}, Passed {tests['totals']['Passed']}, "
                      f"Failed {tests['totals']['Failed']}, Skipped {tests['totals']['Skipped']}, Inconclusive {tests['totals']['Inconclusive']}."]
        for row in tests["reports"]:
            if not row["gatePassed"]:
                lines.append(f"- {link(row['path'])}: {row['gateReason']}.")
        lines += ["", "The outer `success` flag is recorded in the summary but is not sufficient: each report is gated on nested `data.result.Summary` counts, nested `data.result.success` and an EditMode run. Full SHA-256 values are retained in `tests.reports[].sha256`.", ""]
    profiles = summary["optimized"]["performanceProfiles"]
    lines += ["## Complementary profiler — terrain-performance-2 validation", ""]
    if not profiles["reportCount"]:
        lines += ["Expected input: `optimized/report.json` (terrain-performance-2). Not present yet; no profiler row counts, verdict or stage medians are inferred.", ""]
    else:
        lines += ["| Report | Version | Passed | Verdict | Rows | Summaries | Cache probes | Prewarm rows | Probe cells | Alloc counter | Schema | SHA-256 (first 12) |",
                  "|---|---:|---|---:|---:|---:|---:|---:|---|---|---|---|"]
        for check in profiles.get("schemaChecks", []):
            verdict = check["verdict"] if isinstance(check["verdict"], str) else "missing"
            passed = str(check["passed"]).lower() if isinstance(check["passed"], bool) else "missing"
            cells = check["prewarmProbeCells"] if check["prewarmProbeCells"] is not None else "—"
            marker = check.get("allocationCounterAvailable")
            marker_text = "false" if marker is False else ("true" if marker is True else "missing")
            lines.append(f"| {link(check['path'])} | {check['version']} | {passed} | {short(verdict, 46)} | "
                         f"{check['rowCount']} | {check['summaryCount']} | {check['cacheProbeCount']} | {check['prewarmRowCount']} | "
                         f"{cells} | {marker_text} | {'validated' if check['schemaValidated'] else 'pending'} | `{check['sha256'][:12]}` |")
        lines += ["", f"Profiler schema gate: **{'met' if profiles.get('gateSatisfied') else 'pending'}**; "
                      f"{profiles.get('validatedProfileCount', 0)} of {profiles['reportCount']} report(s) schema-validated. "
                      "The check requires top-level passed/verdict/failures, contract/source/scene/physics/cache flags, non-empty rows/summaries/cache/prewarm, row quiet/conservation flags, cache activation flags, the synthetic prewarm probe, dataOnly, tiny and density. Legacy terrain-performance-1 is recognized and summarized but is not gate-validated."]
        for check in profiles.get("schemaChecks", []):
            if not check["schemaValidated"]:
                for reason in check["reasons"][:6]:
                    lines.append(f"- {link(check['path'])}: {reason}")
        lines += ["", "Each measured operation stage keeps its own `stage` path in the summary; stages are never pooled into a frame budget or one silent total.", ""]
    lines += ["Editor coverage note: the lifecycle `OnDestroy` callback is invoked reflectively in EditMode; this does not cover Play-mode automatic destruction dispatch.", ""]
    lines += ["## Matched optimized VerifyAssets — five same-probe runs", ""]
    if not matched["reportCount"]:
        lines += [f"Expected inputs: {' · '.join(link(path) for path in matched['expectedRuns'])}. Not yet produced, so no matched table, delta, cold baseline, FPS or speedup is shown."]
    else:
        lines += ["| Run | Passed | Report duration ms | SHA-256 (first 12) |", "|---|---:|---:|---|"]
        for report in sorted(verify["reports"], key=lambda r: (r["runIndex"] is None, r["runIndex"] or 0, r["path"])):
            run_name = report["runIndex"] if report["runIndex"] is not None else Path(report["path"]).stem
            lines.append(f"| {link(report['path'], str(run_name))} | {str(report['passed']).lower()} | {fmt_ms(report['durationMs'])} | `{report['sha256'][:12]}` |")
        if matched["missingRuns"]:
            lines += ["", "Missing: " + " · ".join(link(path) for path in matched["missingRuns"]) + "."]
        lines += ["", f"Group status: **{matched['status']}** — {matched['statusReason']}"]
        lines += ["", "### Matched warm-cache lease audit", ""]
        if not lease.get("reportCount"):
            lines += ["Expected input: `matched-cache-lease-audit.json` (10 explicit warm leases, ownership restored). Not present; the matched gate stays pending.", ""]
        else:
            first_lease = lease["reports"][0]
            lines += [f"Explicit warm leases: **{lease.get('explicitWarmLeases')}**; preparation **{fmt_ms(lease.get('warmLeasePreparationMs'))} ms**; "
                      f"ownership restored: **{str(lease.get('ownershipRestored')).lower()}**; rows: **{first_lease['rowCount']}**.", "",
                      "| Asset | Users before | Users after | Ownership restored |", "|---|---:|---:|---|"]
            for row in first_lease["rows"]:
                lines.append(f"| {row['asset']} | {row['usersBefore']} | {row['usersAfter']} | {str(row['ownershipRestored']).lower()} |")
            for report in lease["reports"]:
                for reason in report["reasons"][:5]:
                    lines.append(f"- {link(report['path'])}: {reason}")
            lines += ["", "Lease preparation is **outside** the five warm VerifyAssets phase timings and is **not subtracted or removed**. Prewarm construction is measured separately by the prewarm audit. Cleanup is also outside phased timings, so whole-report duration includes different cleanup work from the preserved baseline; its delta is suppressed (raw per-group durations remain descriptive).", ""]
        if matched["identityDifferences"]:
            lines += ["", "| Run | Field | Baseline identity | Matched identity |", "|---|---|---|---|"]
            for diff in matched["identityDifferences"]:
                lines.append(f"| {link(diff['run'], Path(diff['run']).name)} | `{diff['field']}` | {short(diff['baseline'])} | {short(diff['matched'])} |")
        if matched["complete"] and comparison.get("status") == "NOT_YET_MEASURED":
            lines += ["", "No comparison: " + comparison["reason"]]
        if ready:
            lines += ["", "### Initialization and integrity (warm templates)", "",
                      "Pairs are **baseline → matched (matched − baseline)**. Counts are medians; they are not required to stay equal because the optimized runtime may legitimately change mounted meshes or colliders.", "",
                      "| Asset | Cells | Vertices | Colliders | First instance ms | Repeat instance ms |",
                      "|---|---:|---:|---:|---:|---:|"]
            for name in b["assets"]:
                group = verify["assets"].get(name)
                if not group:
                    continue
                asset_path = b["assets"][name]["labels"].get("path", [""])[0]
                label = link(asset_path, name) if asset_path else name
                cells = pair_text(compare_row(comparison, "assets", name, "cellCount"), 0)
                verts = pair_text(compare_row(comparison, "assets", name, "meshVertexCount"), 0)
                colliders = pair_text(compare_row(comparison, "assets", name, "colliderCount"), 0)
                first = pair_text(compare_row(comparison, "assets", name, "firstBuildMs"))
                repeat = pair_text(compare_row(comparison, "assets", name, "repeatBuildMs"))
                lines.append(f"| {label} | {cells} | {verts} | {colliders} | {first} | {repeat} |")
            lines += ["", "### First lethal hit (no sustained stream)", "",
                      "`damageMs = applyDamageMs + flushMs`; the total includes chunk renderer activation on that first hit. Do not replace it with `applyDamageMs` alone.", "",
                      "| Probe asset | Apply ms | Flush ms | Total damage ms | Colliders after | Fragments |",
                      "|---|---:|---:|---:|---:|---:|"]
            for name in b["singleCellDamage"]:
                group = verify["singleCellDamage"].get(name)
                if not group:
                    continue
                asset_path = b["assets"].get(name, {}).get("labels", {}).get("path", [""])[0]
                label = link(asset_path, name) if asset_path else name
                lines.append(f"| {label} | {pair_text(compare_row(comparison, 'singleCellDamage', name, 'applyDamageMs'))} | "
                             f"{pair_text(compare_row(comparison, 'singleCellDamage', name, 'flushMs'))} | "
                             f"{pair_text(compare_row(comparison, 'singleCellDamage', name, 'damageMs'))} | "
                             f"{pair_text(compare_row(comparison, 'singleCellDamage', name, 'collidersAfter'), 0)} | "
                             f"{pair_text(compare_row(comparison, 'singleCellDamage', name, 'fragments'), 0)} |")
            lines += ["", "| Other matched phases | Baseline → matched (Δ) |", "|---|---|",
                      f"| Cross-section cut damage ms | {pair_text(compare_row(comparison, 'cut', None, 'damageMs'))} |",
                      f"| Cross-section cut flush ms | {pair_text(compare_row(comparison, 'cut', None, 'flushMs'))} |",
                      f"| Cross-section removed cells | {pair_text(compare_row(comparison, 'cut', None, 'removedCells'), 0)} |",
                      f"| Density average init ms | {pair_text(compare_row(comparison, 'density', None, 'averageInitMs'))} |",
                      f"| Patch init total ms | {pair_text(compare_row(comparison, 'patchPhysics', None, 'initTotalMs'))} |",
                      f"| Patch average step ms | {pair_text(compare_row(comparison, 'patchPhysics', None, 'stepAvgMs'))} |",
                      f"| Patch maximum step ms | {pair_text(compare_row(comparison, 'patchPhysics', None, 'stepMaxMs'))} |",
                      "", "`n` per metric is retained in the JSON. Metrics measured by only one group appear under `metricCoverage` without a fabricated delta (for example new chunk-renderer instrumentation has no baseline counterpart).",
                      "Whole-report duration is **not differenced**: explicit warm-lease preparation/cleanup work differs from the preserved baseline. Raw per-group duration medians remain in the summary."]
    lines += ["", "## Baseline provenance and limits", "",
              f"- {b['reportCount']} fresh unchanged-runtime runs; original {link('tools/MiningStationRound2/Verification.cs', 'VerifyAssets')} invoked by {link('tools/TerrainPerformance/Baseline.cs', 'TerrainBaseline')} against the unchanged v04 assets.",
              f"- All reports passed with no failures/unsupported sections: **{str(b['allReportsPassed']).lower()}**.",
              f"- Unity {' / '.join(env.get('unityVersion', []))}, {' / '.join(env.get('platform', []))}, {' / '.join(env.get('processor', []))}; GPU {' / '.join(env.get('gpu', []))}. Editor edit mode, not a Player build.",
              "- All timings below are **CPU milliseconds**, shown as **median (minimum–maximum)** across five separate probe runs in the same Editor session. They are not rendered frame times or FPS. No outliers were discarded.",
              "- Template caches were warm in these runs. `firstBuildMs` is the first instance in that probe, **not** cold initialization; per-asset cache flags are retained in the summary.",
              "- Damage is one lethal surface-cell hit on each fresh representative instance plus a separate cut probe. This is not sustained fire or full-station combat.", "",
              "Raw reports: " + " · ".join(link(r["path"]) for r in b["reports"]) + ".", "",
              "## Initialization — reported separately from damage", "",
              "Cold template prewarm/build: **not measured by VerifyAssets, baseline or matched**. The separate prewarm audit measures a fresh private template cache; it is not a cold VerifyAssets baseline and no startup FPS follows from it.", "",
              "| Asset | Cells | Vertices | Colliders | First instance ms | Repeat instance ms |",
              "|---|---:|---:|---:|---:|---:|"]
    for name, group in b["assets"].items():
        asset_path = group["labels"].get("path", [""])[0]
        label = link(asset_path, name) if asset_path else name
        lines.append(f"| {label} | {stable_value(group, 'cellCount')} | {stable_value(group, 'meshVertexCount')} | {stable_value(group, 'colliderCount')} | {display_stat(group, 'firstBuildMs')} | {display_stat(group, 'repeatBuildMs')} |")
    lines += ["", "## First lethal hit — no sustained stream", "",
              "`damageMs = applyDamageMs + flushMs`. Flush includes destruction work; do not substitute only the tiny apply-call cost for the total hit cost.", "",
              "| Probe asset | Apply ms | Flush ms | Total damage ms |", "|---|---:|---:|---:|"]
    for name, group in b["singleCellDamage"].items():
        asset_path = b["assets"].get(name, {}).get("labels", {}).get("path", [""])[0]
        label = link(asset_path, name) if asset_path else name
        lines.append(f"| {label} | {display_stat(group, 'applyDamageMs')} | {display_stat(group, 'flushMs')} | {display_stat(group, 'damageMs')} |")
    cut = b["cut"]
    patch = b["patchPhysics"]
    lines += ["", f"Separate OpenTruss cross-section cut: {stable_value(cut, 'removedCells')} removed cells; {stable_value(cut, 'aliveTotal')} surviving cells; {stable_value(cut, 'islandsAfter')} islands / {stable_value(cut, 'bodiesAfter')} bodies / {stable_value(cut, 'fragments')} newly created fragment. Total damage ms: **{display_stat(cut, 'damageMs')}**; flush ms: {display_stat(cut, 'flushMs')}.", "",
              "**Sustained hits: not measured in the original baseline.** Repeating independent first-hit probes is not a sustained-hit benchmark.", "",
              "## Warm density initialization and isolated physics", "",
              f"- Eight shared-template debris instances: average initialization ms **{display_stat(b['density'], 'averageInitMs')}** per instance.",
              f"- Whole local density patch: {stable_value(patch, 'bodies')} bodies, {stable_value(patch, 'totalCells')} cells, {stable_value(patch, 'totalColliders')} colliders, {stable_value(patch, 'uniqueMeshes')} shared meshes.",
              f"- Patch total warm initialization ms: **{display_stat(patch, 'initTotalMs')}**; first-copy-per-asset subtotal: {display_stat(patch, 'firstPerAssetInitMs')} (already warm templates); remaining-copy subtotal: {display_stat(patch, 'warmInitMs')}.",
              f"- {stable_value(patch, 'steps')} isolated PhysX steps per run at 1/60 s: per-run average CPU ms/step **{display_stat(patch, 'stepAvgMs')}**; per-run maximum step CPU ms **{display_stat(patch, 'stepMaxMs')}**. The latter is a distribution of five run maxima, not raw-step percentiles.",
              f"- NaN count: {stable_value(patch, 'nanCount')}; out-of-bounds bodies: {stable_value(patch, 'outOfBoundsBodies')}; new world-AABB overlap pairs: {stable_value(patch, 'newOverlapPairs')}. Initial/end world-AABB overlap pairs: {stable_value(patch, 'overlapPairsBefore')}/{stable_value(patch, 'overlapPairsAfter')} (coarse AABB checks, not an exact no-contact proof).",
              "- No rendering, AI, gameplay, continued fire or complete station is included in that physics probe.", "",
              "## Prewarm memory and first-hit renderer cost — kept explicit", "",
              "- **Prewarm extra native mesh memory.** The prewarm audit clones each source into a fresh private cache key. `aggregateUnityReportedBytes` (Unity-reported aggregate template mesh) and `chunkMeshStreamLowerBoundBytes` (estimated chunk mesh stream) are different native measurements: they are not an exact total, not additive with managed allocation bytes (withheld here when the counter is unsupported), and not part of any damage millisecond. The v2 profiler's prewarm rows likewise estimate `aggregateEstimatedBytes`, `chunkEstimatedBytes` and their `combinedEstimatedBytes`; those are mesh-stream estimates, not an exact native total. Prewarm creates no renderer, so it never measures draw calls.",
              "- **First-hit renderer activation.** Creating chunk renderer objects, uploading chunk meshes and setting up draw calls on the first lethal hit is charged to that hit's `flushMs`/`damageMs`. A separate activation field, if a report adds one, is a breakdown of the same hit — not a cost that may be removed from the hit total.",
              "- **Warm templates are not cold startup.** Every VerifyAssets run, baseline or matched, starts with a warm template cache; `firstBuildMs` is the first instance inside that probe. Cold prewarm exists only in the separate audit. When that audit is present, its values are in the summary under `optimized.prewarmAudit` and are never folded into a damage or FPS number.", "",
              "## Allocation counter — unavailable, not zero", ""]
    if allocation.get("available") is False:
        lines += [f"- Positive control allocated **{fmt_bytes(allocation.get('positiveControlBytes'))} bytes**; `GC.GetAllocatedBytesForCurrentThread()` observed **{fmt_bytes(allocation.get('observedDeltaBytes'))}** while Mono used heap changed by **{fmt_bytes(allocation.get('monoUsedHeapDeltaBytes'))} bytes**. The counter is unsupported on this Editor.", ""]
    elif allocation.get("available") is True:
        lines += [f"- Positive control allocated **{fmt_bytes(allocation.get('positiveControlBytes'))} bytes** and the counter observed **{fmt_bytes(allocation.get('observedDeltaBytes'))}**; allocation metrics are retained as measured.", ""]
    else:
        lines += ["- No positively validated allocation-counter audit is present; managed allocation bytes are withheld and no zero-allocation or zero-GC claim is made.", ""]
    lines += ["- `allocatedBytes`, `allocatedManagedBytes`, `medianAllocatedBytes`, idle-allocation fields and GC generation counts are suppressed unless the positive control proves the counter works. Sentinels (`-1`) are never reported as medians, and a raw `0` is not treated as a measurement while unavailable.",
              "- Raw pre-fix prewarm zeros are preserved under Temp for audit only and are not evidence. The final guarded prewarm records `allocationCounterAvailable=false` with explicit `-1` row sentinels.",
              f"- Prewarm allocation state: **{prewarm_allocation.get('state', 'unknown')}** ({prewarm_allocation.get('reason', 'not evaluated')}); profiler allocation state: **{profile_allocation.get('state', 'unknown')}** ({profile_allocation.get('reason', 'not evaluated')}).",
              "- Native/Unity mesh memory and chunk-stream estimates (`aggregateUnityReportedBytes`, `chunkMeshStreamLowerBoundBytes`, v2 `aggregateEstimatedBytes`, `chunkEstimatedBytes`) remain distinct reported measurements; they are not managed allocations and are never added to them.",
              "- The CPU evidence gate does not depend on the allocation counter; unsupported allocation telemetry cannot block the Editor CPU label, and it is never replaced by zero.", "",
              "## Hash protection", "",
              f"The initial snapshot was captured at {audit['snapshotUtc']} before this analyzer agent's first file edit. {len(audit['sourceFiles'])} original source files match the historical {link(audit['referenceManifest'], 'source-hash manifest')}; the immutable snapshot also covers {len(audit['generatedV04Files'])} generated-v04 asset/layout/meta files and {len(audit['preservedEvidenceFiles'])} preserved v04/baseline evidence files, {snapshot_total} files in total."]
    latest_comparison = audit.get("latestComparison")
    if latest_comparison:
        lines.append(f"Latest read-only comparison at {latest_comparison['checkedUtc']}: **{'UNCHANGED' if latest_comparison['passed'] else 'INTEGRITY FAILURE'}**, {latest_comparison['checkedCount']} checked files. This proves preservation at that check only, not a future state.")
    lines += ["", f"The immutable snapshot is not extended with the matched or test reports; their SHA-256 values are recorded in the summary (`matchedVerifyAssets.verify.reports` and `tests.reports`), so this {snapshot_total}-file snapshot still describes only the pre-fix state.", "",
              "## Remaining gates and standing constraints", ""]
    if pending_names:
        lines += [f"Final status is **IN PROGRESS**; pending: {', '.join(pending_names)}. Each pending input is absent or failing, never replaced with another probe's numbers.", ""]
        for check in gate["checks"]:
            if not check["satisfied"]:
                lines.append(f"- **{gate_names.get(check['id'], check['id'])}** — {check['requirement']}; expected `{check['expected']}`. Observed: {check['observed']}.")
        lines.append("")
    else:
        lines += ["All evidence gates above are met. The SCOPED VALIDATED label still covers only the listed Editor CPU evidence, not map assembly or FPS.", ""]
    lines += ["- Record functional gates separately: HP/reset semantics, cell conservation, exact cuboid collision coverage, local connectivity certificate plus full-oracle fallback, and the tiny-fragment policy conditions.",
              "- Confirm the lazy chunk-render activation, the retained single root renderer for intact assets, subtracted/reused collision shapes with bounded recompaction, and quiet sparse HP expiry without geometry respawn.",
              "- Keep tiny-fragment rendering **OFF by default (`maxCells = 0`)**. If it is ever enabled, require occupied-cell count ≤ 4 **and** full occupied world-bounds maximum dimension ≤ 2 world units for **newly detached** fragments only; no age/distance removal.",
              "- Keep prewarm native mesh memory and first-hit renderer activation/draw-call cost explicit; do not net them out of damage ms.",
              "- Recheck protected hashes after implementation and measurements. No map assembly is authorized by this document.", "",
              "## Reproduce analysis without Unity", "",
              "Run from the project root (standard-library Python only):", "", "```powershell",
              "python tools/TerrainPerformance/Summarize.py --self-test",
              "python tools/TerrainPerformance/Summarize.py",
              "# The five matched runs and tests/*.json are discovered when present:",
              "python tools/TerrainPerformance/Summarize.py --matched Documentation/TerrainPerformance-2026-11-01/optimized-matched",
              "python tools/TerrainPerformance/Summarize.py --tests Documentation/TerrainPerformance-2026-11-01/tests",
              "# Allocation-counter positive-control audit (also auto-discovered):",
              "python tools/TerrainPerformance/Summarize.py --allocation-audit Documentation/TerrainPerformance-2026-11-01/allocation-counter-audit.json",
              "# Matched warm-cache lease audit (also auto-discovered):",
              "python tools/TerrainPerformance/Summarize.py --lease-audit Documentation/TerrainPerformance-2026-11-01/matched-cache-lease-audit.json",
              "# Complementary profiler and prewarm inputs are separate groups; never mix them with matched:",
              "python tools/TerrainPerformance/Summarize.py --optimized Documentation/TerrainPerformance-2026-11-01/optimized",
              "python tools/TerrainPerformance/Summarize.py --optimized Documentation/TerrainPerformance-2026-11-01/prewarm-audit.json",
              "# Recheck hashes without regenerating the summary or README.",
              "python tools/TerrainPerformance/Summarize.py --audit-only", "```", "",
              "Running the analyzer refreshes only this README, the summary and the latest audit comparison; it never resets the initial hash snapshot. Missing metrics stay absent, not zero; no cold baseline, frame time or FPS is inferred. Final status becomes SCOPED VALIDATED (CPU only) only when every evidence gate is met.", ""]
    return "\n".join(lines)


def self_test():
    assert stats([3, 1, 2]) == {"n": 3, "median": 2, "min": 1, "max": 3, "samples": [3, 1, 2]}
    assert stats([1, 2])["median"] == 1.5
    group = aggregate_records([{"ms": 2, "flag": True}, {"flag": False}])
    assert group["metrics"]["ms"]["n"] == 1 and "flag" not in group["metrics"]
    data = {"probes": [{"name": "A", "phase": "first", "durationMs": 9},
                       {"name": "A", "phase": "sustained", "samplesMs": [2, 4]},
                       {"name": "B", "phase": "first", "durationMs": 15}],
            "samples": [{"elapsed_ms": 3, "success": True}, {"elapsed_ms": 5}],
            "bytes": 64, "unitTiming": {"unit": "ms", "value": 7}}
    leaves = list(numeric_leaves(data))
    assert any("name=A;phase=first" in p and v == 9 for p, _, _, v in leaves)
    assert [v for p, _, _, v in leaves if "phase=sustained" in p] == [2, 4]
    assert [v for p, _, _, v in leaves if p == "samples[*].elapsed_ms"] == [3, 5]
    assert not any(p.endswith("success") for p, _, _, _ in leaves)
    assert timing_field("durationMs", None) and timing_field("value", "ms")
    assert not timing_field("allocationBytes", None)
    for invalid in ([True], [], [float("nan")], [float("inf")]):
        try:
            stats(invalid)
        except ValueError:
            pass
        else:
            raise AssertionError(f"Accepted invalid statistics: {invalid}")
    assert run_index(Path("run-3.json")) == 3 and run_index(Path("report.json")) is None
    assert is_prewarm_report(Path("prewarm-audit.json"), {"measurement": "m", "rows": []})
    assert not is_prewarm_report(Path("report.json"), {"version": "terrain-performance-1", "rows": []})

    with tempfile.TemporaryDirectory() as temp:
        root = Path(temp)

        def fake_report(first, damage, assets=("A", "B"), extra=None):
            report = {
                "version": "mining-round2-verification/1", "passed": True,
                "failures": [], "unsupported": [], "generatedUtc": "2026-01-01T00:00:00Z",
                "durationMs": 100.0, "environment": {"unityVersion": "test"},
                "layoutPath": "L", "layout": {"path": "L", "instanceCount": 2},
                "source": {"changedAssets": 0},
                "assets": [{"name": name, "path": "P/" + name, "cellCount": 10,
                            "meshVertexCount": 100, "colliderCount": 1,
                            "firstBuildMs": first, "repeatBuildMs": first / 2} for name in assets],
                "representatives": [{"assetName": assets[0], "applyDamageMs": 0.1,
                                     "flushMs": damage, "damageMs": damage + 0.1}],
                "cut": {"assetName": assets[0], "removedCells": 5, "damageMs": damage, "flushMs": damage},
                "density": {"averageInitMs": first},
                "physics": {"nanChecks": 12, "steps": 120},
                "patchPhysics": {"initTotalMs": 50.0, "stepAvgMs": 0.5, "nanCount": 0},
            }
            if extra:
                report["representatives"][0].update(extra)
            return report

        baseline_dir, matched_dir = root / "baseline", root / "matched"
        baseline_dir.mkdir()
        matched_dir.mkdir()
        baseline_paths = []
        for index in range(MATCHED_RUN_COUNT):
            path = baseline_dir / f"run-{index}.json"
            write_json(path, fake_report(1.0, 10.0))
            baseline_paths.append(path)
        baseline = summarize_baseline(baseline_paths)
        assert baseline["reportCount"] == 5 and baseline["allReportsPassed"] is True

        matched_paths = []
        for index in range(MATCHED_RUN_COUNT):
            path = matched_dir / f"run-{index}.json"
            write_json(path, fake_report(2.0, 20.0, extra={"rendererActivationMs": 0.5}))
            matched_paths.append(path)
        matched = summarize_matched(matched_paths, baseline, [])
        assert matched["status"] == "READY_FOR_REVIEW" and matched["complete"] is True
        assert matched["compatible"] is True and matched["comparison"]["status"] == "DESCRIPTIVE_ONLY"
        rows = matched["comparison"]["deltas"]
        delta = next(r for r in rows if r["phase"] == "assets" and r["identity"] == "A" and r["metric"] == "firstBuildMs")
        assert delta["baselineMedian"] == 1.0 and delta["matchedMedian"] == 2.0 and delta["delta"] == 1.0
        assert not any(r["metric"] == "rendererActivationMs" for r in rows)
        assert not any(r["metric"] == "durationMs" for r in rows)
        assert matched["comparison"]["durationComparabilityException"]["deltaSuppressed"] is True
        coverage = next(c for c in matched["comparison"]["metricCoverage"] if c["phase"] == "singleCellDamage")
        assert "rendererActivationMs" in coverage["matchedOnly"]

        partial = summarize_matched(matched_paths[:2], baseline, [])
        assert partial["status"] == "PARTIAL_IN_PROGRESS" and partial["comparison"]["status"] == "NOT_YET_MEASURED"
        assert summarize_matched([], baseline, [])["status"] == "NOT_YET_MEASURED"

        incompatible_paths = []
        for index in range(MATCHED_RUN_COUNT):
            path = matched_dir / f"other-{index}.json"
            write_json(path, fake_report(2.0, 20.0, assets=("A", "C")))
            incompatible_paths.append(path)
        incompatible = summarize_matched(incompatible_paths, baseline, [])
        assert incompatible["compatible"] is False and incompatible["comparison"]["status"] == "NOT_COMPARABLE"

        failed_paths = []
        for index in range(MATCHED_RUN_COUNT):
            path = matched_dir / f"failed-{index}.json"
            report = fake_report(2.0, 20.0)
            report["passed"] = index != 0
            write_json(path, report)
            failed_paths.append(path)
        failed = summarize_matched(failed_paths, baseline, [])
        assert failed["status"] == "READY_WITH_FAILURES" and failed["comparison"]["status"] == "NOT_COMPARABLE"

        def fake_profiler_v2(passed=True, conserved=True):
            return {
                "version": "terrain-performance-2", "passed": passed,
                "verdict": "FUNCTIONAL CHECKS PASSED — test" if passed else "FAILED — test",
                "failures": [] if passed else ["failure"],
                "sceneRestored": True, "globalPhysicsUntouched": True,
                "templateCachePreserved": True, "templateCacheRestored": True,
                "allocation": {"methodAvailable": True, "supported": False,
                               "positiveControlBytes": 1048576, "attempts": 1,
                               "firstBefore": 0, "firstAfter": 0, "firstDelta": 0, "maxDelta": 0,
                               "heapBefore": 0, "heapAfter": 1052672, "note": "positive control"},
                "contract": {"matchesContract": True, "chunkEdge": 4, "minimumCells": 512},
                "source": {"integrityPassed": True},
                "files": [{"path": "P", "before": "A", "after": "A", "unchanged": True}],
                "rows": [
                    {"asset": "A", "role": "station", "scenario": "single-lethal", "run": 0,
                     "chunkSupported": True, "conserved": conserved, "uniquePhysicalOwners": True,
                     "physicsBodiesMatchRuntimes": True, "runtimeEnabledAfter": False,
                     "intactSingleRootRenderer": True, "firstHitIncludesRendererActivation": True,
                     "operations": [{"stage": "apply", "cpuMs": 1.0, "allocatedBytes": -1, "gen0": 0},
                                    {"stage": "flush", "cpuMs": 2.0, "allocatedBytes": -1, "gen0": 0}],
                     "idleAllocatedBytes": -1, "initializeMs": 1.0, "firstHitTotalMs": 3.0},
                    {"asset": "A", "role": "station", "scenario": "idle-1024", "run": 0,
                     "chunkSupported": True, "conserved": True, "uniquePhysicalOwners": True,
                     "physicsBodiesMatchRuntimes": True, "runtimeEnabledAfter": False,
                     "intactSingleRootRenderer": True, "noRebuild": True,
                     "operations": [{"stage": "maintenance", "cpuMs": 0.5, "allocatedBytes": -1}], "initializeMs": 1.0},
                    {"asset": "A", "role": "station", "scenario": "nonlethal-expiry", "run": 0,
                     "chunkSupported": True, "conserved": True, "uniquePhysicalOwners": True,
                     "physicsBodiesMatchRuntimes": True, "runtimeEnabledAfter": False,
                     "intactSingleRootRenderer": True, "noRebuild": True, "healthRestored": True,
                     "operations": [{"stage": "expiry-maintenance", "cpuMs": 0.2, "allocatedBytes": -1}], "initializeMs": 1.0},
                ],
                "summaries": [
                    {"asset": "A", "scenario": "single-lethal", "stage": "apply", "samples": 1,
                     "medianMs": 1.0, "medianAllocatedBytes": -1},
                    {"asset": "A", "scenario": "single-lethal", "stage": "flush", "samples": 1,
                     "medianMs": 2.0, "medianAllocatedBytes": -1},
                ],
                "cache": [{"asset": "A", "sharedTemplates": True, "aggregateShared": True,
                           "intactSingleRootRenderer": True, "chunksActivateOnFirstLethal": True,
                           "peerIntactAfterHit": True}],
                "prewarm": [{"asset": "A", "cells": 585, "chunks": 10}],
                "prewarmProbe": {"cells": 585, "chunks": 10, "reusedOnSecondInstance": True,
                                 "intactSingleRootRenderer": True, "noChunkObjectsBeforeHit": True,
                                 "chunksActivateOnFirstLethal": True, "chunkCountMatchesMountedMeshes": True,
                                 "rootAggregateHidden": True, "peerStayedIntact": True,
                                 "prewarmedResourcesReused": True, "cacheRestored": True},
                "dataOnly": {"dataOnly": True, "supported": False, "renderCacheAbsent": True, "renderMeshesEmpty": True,
                             "noTemplate": True, "noChunks": True, "noMountedMesh": True,
                             "rendererDisabled": True, "noColliders": True, "noRigidbody": True,
                             "cacheStillAbsentAfterDamage": True, "removed": 1},
                "tiny": {"apiPresent": True, "defaultOff": True, "defaultPhysical": True,
                         "optInStored": True, "retainedLargePhysical": True,
                         "extentAboveBoundaryPhysical": True, "conserved": True},
                "density": {"instances": 2, "initializeMs": 1.0, "render": {"runtimes": 2}},
            }

        profile = root / "report.json"
        write_json(profile, fake_profiler_v2())
        legacy = root / "legacy-report.json"
        write_json(legacy, {"version": "terrain-performance-1", "rows": [{"name": "x", "ms": 1.0}]})
        invalid_profile = root / "invalid-report.json"
        write_json(invalid_profile, fake_profiler_v2(passed=False, conserved=False))
        prewarm = root / "prewarm-audit.json"
        write_json(prewarm, {"measurement": "m", "allocationCounterAvailable": False,
                             "allocationPositiveControlBytes": 1048576, "allocationObservedDeltaBytes": 0,
                             "rows": [{"asset": f"A{index}", "coldPrivateTemplateMs": 2.0,
                                       "allocatedManagedBytes": -1} for index in range(10)]})
        optimized = summarize_optimized([profile, prewarm])
        profiles = optimized["performanceProfiles"]
        assert profiles["reportCount"] == 1
        assert profiles["validatedProfileCount"] == 1 and profiles["gateSatisfied"] is True
        assert profiles["allocationEvidence"]["state"] == "unavailable"
        assert profiles["schemaChecks"][0]["allocationCounterAvailable"] is False
        assert profiles["schemaChecks"][0]["allocationObservedDeltaBytes"] == 0
        assert allocation_state({"allocation": {"methodAvailable": True, "supported": False}})[0] is False
        assert allocation_state({"allocation": {"methodAvailable": True, "supported": True,
                                                "positiveControlBytes": 1048576,
                                                "maxDelta": 1048576}})[0] is True
        assert allocation_state({"allocation": {"methodAvailable": True, "supported": True,
                                                "positiveControlBytes": 1048576,
                                                "firstDelta": 1048576}})[0] is True
        assert allocation_state({"dataOnly": {"supported": False}})[0] is None
        profile_keys = list(profiles["timingsMs"]) + list(profiles["otherNumericFields"])
        assert not any("allocat" in key.lower() for key in profile_keys)
        stage_paths = set(profiles["timingsMs"])
        assert any("stage=apply" in path for path in stage_paths) and any("stage=flush" in path for path in stage_paths)
        legacy_summary = summarize_optimized([legacy])["performanceProfiles"]
        assert legacy_summary["reportCount"] == 1 and legacy_summary["validatedProfileCount"] == 0
        assert legacy_summary["gateSatisfied"] is False
        invalid_summary = summarize_optimized([invalid_profile])["performanceProfiles"]
        assert invalid_summary["validatedProfileCount"] == 0
        assert any("conserved" in reason for reason in invalid_summary["schemaChecks"][0]["reasons"])
        assert optimized["prewarmAudit"]["reportCount"] == 1
        assert optimized["prewarmAudit"]["rowCountTotal"] == 10
        assert optimized["prewarmAudit"]["allocationEvidence"]["state"] == "unavailable"
        prewarm_keys = list(optimized["prewarmAudit"]["timingsMs"]) + list(optimized["prewarmAudit"]["otherNumericFields"])
        assert not any("allocat" in key.lower() for key in prewarm_keys)
        assert optimized["sameProbeVerifyAssets"] is None
        old_bad = root / "old-bad-prewarm.json"
        write_json(old_bad, {"measurement": "m",
                             "rows": [{"asset": f"A{index}", "allocatedManagedBytes": 0} for index in range(3)]})
        old_summary = summarize_optimized([old_bad], {"available": None, "reason": "no audit"})["prewarmAudit"]
        assert old_summary["allocationEvidence"]["state"] == "unknown"
        old_keys = list(old_summary["timingsMs"]) + list(old_summary["otherNumericFields"])
        assert not any("allocat" in key.lower() for key in old_keys)
        assert is_allocation_field("allocatedManagedBytes") and is_allocation_field("gen0")
        assert not is_allocation_field("latencyMs")

        probe = root / "allocation-counter-probe.json"
        write_json(probe, {"success": True, "data": {"result": {"result": "positiveControlBytes=1048576; "
                                                                 "threadCounterBefore=0; threadCounterAfter=0; "
                                                                 "threadDelta=0; heapUsedDelta=1052672; supported=False"}}})
        audit = summarize_allocation_audit([probe])
        assert audit["available"] is False and audit["positiveControlBytes"] == 1048576
        assert audit["observedDeltaBytes"] == 0 and audit["monoUsedHeapDeltaBytes"] == 1052672
        curated = root / "guarded-audit.json"
        write_json(curated, {"allocationCounterAvailable": True, "allocationPositiveControlBytes": 1048576,
                             "allocationObservedDeltaBytes": 1048576, "monoUsedHeapDeltaBytes": 1048576})
        positive_audit = summarize_allocation_audit([curated])
        assert positive_audit["available"] is True
        available_report = fake_profiler_v2()
        available_report["allocation"]["supported"] = True
        available_report["allocation"]["maxDelta"] = 1048576
        available_report["rows"][0]["operations"][0]["allocatedBytes"] = 0
        available_report["rows"][0]["operations"][0]["allocatedSentinelBytes"] = -1
        available_report["summaries"][0]["medianAllocatedBytes"] = 0
        available_path = root / "available-report.json"
        write_json(available_path, available_report)
        available_summary = summarize_optimized([available_path])["performanceProfiles"]
        available_keys = list(available_summary["timingsMs"]) + list(available_summary["otherNumericFields"])
        assert available_summary["allocationEvidence"]["state"] == "available"
        assert available_summary["schemaChecks"][0]["allocationCounterAvailable"] is True
        assert available_summary["schemaChecks"][0]["allocationObservedDeltaBytes"] == 1048576
        assert any("allocatedBytes" in key for key in available_keys)
        assert any("medianAllocatedBytes" in key for key in available_keys)
        assert not any("Sentinel" in key for key in available_keys)
        assert available_summary["allocationEvidence"]["sentinelFields"]
        small_prewarm = root / "small-prewarm.json"
        write_json(small_prewarm, {"measurement": "m",
                                   "rows": [{"asset": "A", "coldPrivateTemplateMs": 2.0}]})
        assert summarize_optimized([small_prewarm])["prewarmAudit"]["rowCountTotal"] == 1
        lease = root / "matched-cache-lease-audit.json"
        write_json(lease, {"explicitWarmLeases": 10, "warmLeasePreparationMs": 12.5,
                           "ownershipRestored": True,
                           "rows": [{"asset": f"A{index}", "usersBefore": 1, "usersAfter": 1,
                                     "ownershipRestored": True} for index in range(10)]})
        lease_summary = summarize_lease_audit([lease])
        assert lease_summary["gateSatisfied"] is True and lease_summary["explicitWarmLeases"] == 10
        assert lease_summary["ownershipRestored"] is True
        invalid_lease = root / "invalid-lease.json"
        write_json(invalid_lease, {"explicitWarmLeases": 9, "warmLeasePreparationMs": 1.0,
                                   "ownershipRestored": False,
                                   "rows": [{"asset": "A", "usersBefore": 1, "usersAfter": 2,
                                             "ownershipRestored": False}]})
        assert summarize_lease_audit([invalid_lease])["gateSatisfied"] is False
        assert summarize_lease_audit([])["status"] == "NOT_YET_MEASURED"

        def fake_test(total, passed, failed=0, skipped=0, inconclusive=0, nested=True, mode="EditMode", outer=True):
            return {"success": outer,
                    "data": {"result": {"Summary": {"Total": total, "Passed": passed, "Failed": failed,
                                                    "Skipped": skipped, "Inconclusive": inconclusive},
                                        "success": nested, "Mode": mode}}}

        tests_dir = root / "tests"
        tests_dir.mkdir()
        assert summarize_tests([])["status"] == "NOT_YET_MEASURED"
        zero_tests = [tests_dir / "zero.json"]
        write_json(zero_tests[0], fake_test(0, 0))
        zero_summary = summarize_tests(zero_tests)
        assert zero_summary["status"] == "IN_PROGRESS" and zero_summary["gateSatisfied"] is False
        failed_tests = [tests_dir / "failed.json"]
        write_json(failed_tests[0], fake_test(66, 46, 20))
        failed_tests_summary = summarize_tests(failed_tests)
        assert failed_tests_summary["status"] == "IN_PROGRESS" and failed_tests_summary["gateSatisfied"] is False
        passed_tests = [tests_dir / "passed.json"]
        write_json(passed_tests[0], fake_test(8, 8))
        passed_tests_summary = summarize_tests(passed_tests)
        assert passed_tests_summary["status"] == "PASSED" and passed_tests_summary["gateSatisfied"] is True
        outer_only = tests_dir / "outer-only.json"
        write_json(outer_only, fake_test(8, 8, nested=False, outer=True))
        assert summarize_tests([outer_only])["status"] == "IN_PROGRESS"
        playmode = tests_dir / "playmode.json"
        write_json(playmode, fake_test(8, 8, mode="PlayMode"))
        assert summarize_tests([playmode])["status"] == "IN_PROGRESS"
        utf16 = tests_dir / "utf16-passed.json"
        utf16.write_bytes(json.dumps(fake_test(3, 3)).encode("utf-16"))
        assert summarize_tests([utf16])["status"] == "PASSED"

        source_file, preserved_file = root / "source.bin", root / "preserved.json"
        source_file.write_bytes(b"source")
        preserved_file.write_bytes(b"evidence")
        generated_root = root / "generated"
        generated_root.mkdir()
        (generated_root / "a.asset").write_bytes(b"asset")
        audit_path = root / "audit.json"
        audit = {"schemaVersion": "terrain-source-hash-audit/1", "snapshotUtc": "2026-01-01T00:00:00+00:00",
                 "referenceManifest": "manifest.json",
                 "sourceFiles": [{"path": str(source_file), "referenceSha256": sha256(source_file),
                                  "initialSha256": sha256(source_file), "matchesReferenceAtSnapshot": True}],
                 "generatedV04Root": str(generated_root),
                 "generatedV04Files": [{"path": relative(generated_root / "a.asset"),
                                        "sha256": sha256(generated_root / "a.asset")}],
                 "preservedEvidenceFiles": [{"path": str(preserved_file), "sha256": sha256(preserved_file)}]}
        write_json(audit_path, audit)
        snapshot = {key: json.loads(audit_path.read_text(encoding="utf-8"))[key] for key in audit}
        refreshed = compare_audit(audit_path)
        assert refreshed["latestComparison"]["passed"] is True
        assert {key: refreshed[key] for key in audit} == snapshot
        source_file.write_bytes(b"changed")
        tampered = compare_audit(audit_path)
        assert tampered["status"] == "INTEGRITY_FAILURE" and tampered["latestComparison"]["passed"] is False
        empty = dict(audit, sourceFiles=[], generatedV04Files=[])
        empty_path = root / "empty.json"
        write_json(empty_path, empty)
        try:
            compare_audit(empty_path)
        except ValueError:
            pass
        else:
            raise AssertionError("compare_audit accepted an empty snapshot")

        gate_audit = {"sourceFiles": [{}] * 10, "generatedV04Files": [{}] * 29,
                      "preservedEvidenceFiles": [{}] * 9,
                      "latestComparison": {"passed": True, "checkedCount": 48}}
        gate = evidence_gate(baseline, matched, passed_tests_summary, optimized, gate_audit, audit_path, lease_summary)
        assert gate["validated"] is True and gate["status"] == "SCOPED_VALIDATED_CPU_ONLY"
        blocked_tests = evidence_gate(baseline, matched, failed_tests_summary, optimized, gate_audit, audit_path, lease_summary)
        assert blocked_tests["validated"] is False and "functionalTests" in blocked_tests["unsatisfied"]
        blocked_prewarm = evidence_gate(baseline, matched, passed_tests_summary,
                                        summarize_optimized([profile, small_prewarm]), gate_audit, audit_path, lease_summary)
        assert blocked_prewarm["validated"] is False and "prewarmAudit" in blocked_prewarm["unsatisfied"]
        blocked_matched = evidence_gate(baseline, partial, passed_tests_summary, optimized, gate_audit, audit_path, lease_summary)
        assert blocked_matched["validated"] is False and "matchedVerifyAssets" in blocked_matched["unsatisfied"]
        blocked_lease = evidence_gate(baseline, matched, passed_tests_summary, optimized, gate_audit, audit_path,
                                      summarize_lease_audit([invalid_lease]))
        assert blocked_lease["validated"] is False and "matchedVerifyAssets" in blocked_lease["unsatisfied"]
        blocked_missing_lease = evidence_gate(baseline, matched, passed_tests_summary, optimized, gate_audit,
                                              audit_path, summarize_lease_audit([]))
        assert blocked_missing_lease["validated"] is False and "matchedVerifyAssets" in blocked_missing_lease["unsatisfied"]
        blocked_hashes = evidence_gate(baseline, matched, passed_tests_summary, optimized,
                                       {"sourceFiles": [{}] * 10, "generatedV04Files": [{}] * 29,
                                        "preservedEvidenceFiles": [{}] * 9,
                                        "latestComparison": {"passed": False, "checkedCount": 48}},
                                       audit_path, lease_summary)
        assert blocked_hashes["validated"] is False and "protectedHashes" in blocked_hashes["unsatisfied"]
    print("PASS: statistics, missing values, phase/probe isolation, timing units, finite values, "
          "matched grouping/comparison with duration suppression, "
          "test-gate missing/zero/failed/passed/nested/Editor cases, "
          "terrain-performance-2 row/collection validation with legacy v1 recognition, "
          "allocation positive-control/sentinel suppression, matched lease-audit gating, "
          "prewarm separation, evidence gate and audit-snapshot immutability")


def default_optimized_inputs():
    specs = []
    if resolve(PROFILE_DIR).is_dir():
        specs.append(str(PROFILE_DIR))
    if resolve(PREWARM_FILE).is_file():
        specs.append(str(PREWARM_FILE))
    return expand_inputs(specs, required=False) if specs else []


def default_tests_inputs():
    return expand_inputs([str(TESTS_DIR / "*.json")], required=False)


def default_allocation_audit_inputs():
    path = resolve(ALLOCATION_AUDIT_FILE)
    return [path] if path.is_file() else []


def default_lease_audit_inputs():
    path = resolve(LEASE_AUDIT_FILE)
    return [path] if path.is_file() else []


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", action="append", help="VerifyAssets file/directory/glob; repeatable")
    parser.add_argument("--matched", action="append",
                        help="Five matched optimized VerifyAssets files/directory/glob; repeatable")
    parser.add_argument("--tests", action="append",
                        help="Final Unity CLI run_tests JSON files/directory/glob; repeatable")
    parser.add_argument("--optimized", action="append",
                        help="Complementary profiler/prewarm JSON file/directory/glob; repeatable")
    parser.add_argument("--allocation-audit", action="append",
                        help="Allocation-counter positive-control audit JSON; repeatable")
    parser.add_argument("--lease-audit", action="append",
                        help="Matched warm-cache lease audit JSON; repeatable")
    parser.add_argument("--summary", default=str(DOCS / "baseline-summary.json"))
    parser.add_argument("--readme", default=str(DOCS / "README.md"))
    parser.add_argument("--audit", default=str(DOCS / "source-hash-audit.json"))
    parser.add_argument("--audit-only", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args(argv)
    if args.self_test:
        self_test()
        return 0
    audit_path = resolve(args.audit)
    audit = compare_audit(audit_path)
    if args.audit_only:
        write_json(audit_path, audit)
        print(f"Hash audit: {audit['status']} ({audit['latestComparison']['checkedCount']} files)")
        return 0 if audit["latestComparison"]["passed"] else 2
    baseline_paths = expand_inputs(args.baseline or [str(DOCS / "baseline/run-*.json")])
    if args.matched:
        matched_paths = expand_inputs(args.matched)
        if all(run_index(path) is not None for path in matched_paths):
            present_names = {path.name for path in matched_paths}
            matched_missing = [relative(path) for path in (resolve(spec) for spec in MATCHED_RUNS)
                               if path.name not in present_names]
        else:
            matched_missing = []
    else:
        resolved = [resolve(spec) for spec in MATCHED_RUNS]
        matched_paths = [path for path in resolved if path.is_file()]
        matched_missing = [relative(path) for path in resolved if not path.is_file()]
    optimized_paths = (expand_inputs(args.optimized) if args.optimized else default_optimized_inputs())
    tests_paths = expand_inputs(args.tests) if args.tests else default_tests_inputs()
    allocation_paths = (expand_inputs(args.allocation_audit) if args.allocation_audit
                        else default_allocation_audit_inputs())
    lease_paths = expand_inputs(args.lease_audit) if args.lease_audit else default_lease_audit_inputs()
    matched_set = set(matched_paths)
    allocation_set = set(allocation_paths)
    lease_set = set(lease_paths)
    excluded_from_optimized = [relative(path) for path in optimized_paths
                               if path in matched_set or path in allocation_set or path in lease_set]
    optimized_paths = [path for path in optimized_paths
                       if path not in matched_set and path not in allocation_set and path not in lease_set]
    summary_path, readme_path = resolve(args.summary), resolve(args.readme)
    output_paths = {summary_path, readme_path, audit_path}
    if len(output_paths) != 3 or output_paths.intersection(baseline_paths + matched_paths + optimized_paths + tests_paths + allocation_paths + lease_paths):
        raise ValueError("Output paths must be distinct and must never overwrite input reports")
    if set(baseline_paths).intersection(matched_paths):
        raise ValueError("Baseline and matched inputs must not overlap")
    if set(baseline_paths).intersection(optimized_paths):
        raise ValueError("Baseline and complementary optimized inputs must not overlap")
    if set(baseline_paths).intersection(tests_paths) or set(matched_paths).intersection(tests_paths):
        raise ValueError("Test inputs must be distinct from baseline and matched inputs")
    baseline_summary = summarize_baseline(baseline_paths)
    matched_summary = summarize_matched(matched_paths, baseline_summary, matched_missing)
    matched_summary["excludedFromOptimized"] = excluded_from_optimized
    tests_summary = summarize_tests(tests_paths)
    allocation_audit = summarize_allocation_audit(allocation_paths)
    lease_audit = summarize_lease_audit(lease_paths)
    optimized_summary = summarize_optimized(optimized_paths, allocation_audit)
    gate = evidence_gate(baseline_summary, matched_summary, tests_summary, optimized_summary,
                         audit, audit_path, lease_audit)
    summary = {"schemaVersion": "terrain-performance-summary/6", "generatedUtc": utc_now(),
               "status": gate["status"], "timingUnit": "CPU milliseconds, not FPS",
               "baseline": baseline_summary,
               "matchedVerifyAssets": matched_summary,
               "matchedLeaseAudit": lease_audit,
               "tests": tests_summary,
               "optimized": optimized_summary,
               "allocationAudit": allocation_audit,
               "gate": gate}
    readme = readme_text(summary, audit, summary_path, audit_path)
    write_json(summary_path, summary)
    write_json(audit_path, audit)
    readme_path.parent.mkdir(parents=True, exist_ok=True)
    readme_path.write_text(readme, encoding="utf-8")
    print(f"Baseline: {baseline_summary['reportCount']} reports; passed={baseline_summary['allReportsPassed']}")
    print(f"Matched VerifyAssets: {matched_summary['reportCount']}/{MATCHED_RUN_COUNT} report(s); status={matched_summary['status']}")
    print(f"Functional tests: {tests_summary['reportCount']} report(s); status={tests_summary['status']}")
    print(f"Complementary optimized: {len(optimized_paths)} report(s); status={optimized_summary['status']}")
    print(f"Profiler schema: {optimized_summary['performanceProfiles'].get('validatedProfileCount', 0)} validated of "
          f"{optimized_summary['performanceProfiles']['reportCount']}; gate={optimized_summary['performanceProfiles'].get('gateSatisfied')}")
    print(f"Allocation audit: {allocation_audit['state']}; {allocation_audit['reason']}")
    print(f"Matched lease audit: {lease_audit['status']}; leases={lease_audit.get('explicitWarmLeases')}; "
          f"ownershipRestored={lease_audit.get('ownershipRestored')}")
    if excluded_from_optimized:
        print(f"Excluded {len(excluded_from_optimized)} matched/allocation input(s) from the complementary group")
    print(f"Evidence gate: {gate['status']}; pending={', '.join(gate['unsatisfied']) or 'none'}")
    print(f"Hash audit: {audit['status']} ({audit['latestComparison']['checkedCount']} files)")
    print(f"Wrote {relative(summary_path)}, {relative(readme_path)}, {relative(audit_path)}")
    return 0 if baseline_summary["allReportsPassed"] and audit["latestComparison"]["passed"] else 2


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        sys.exit(1)
