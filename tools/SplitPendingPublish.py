"""Plan, or explicitly apply, a lossless split of our unpublished large commit.

Default invocation is read-only. --apply creates commit objects and atomically
updates main plus a backup ref; it never changes working files or the real index.
It never pushes, bypasses LFS hooks, resets files, or rewrites a remote ref.
"""

import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile


BASE = "c0346a89b056360c59a6493ddde396e6a86803d9"
SOURCE = "eede49c38fe95f377b4200718968ea23a42f6316"
BRANCH = "refs/heads/main"
BACKUP = "refs/heads/codex/full-before-split-revision7"
LIMIT = int(1.35 * 1024 ** 3)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--apply", action="store_true", help="Create split commits and update local main")
    parser.add_argument("--report", type=Path, help="JSON report path; required with --apply")
    args = parser.parse_args()
    if args.apply and not args.report:
        parser.error("--apply requires --report for a persistent recovery record")
    repo = args.repo.resolve()
    git_base = ["git", "-c", "safe.directory=" + repo.as_posix(), "-c", "gc.auto=0"]

    def git(*command, data=None, env=None):
        result = subprocess.run(git_base + list(command), cwd=repo, input=data,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env)
        if result.returncode:
            raise RuntimeError("git " + " ".join(command) + ": " +
                               result.stderr.decode("utf-8", "replace").strip())
        return result.stdout

    def value(*command):
        return git(*command).decode("utf-8").strip()

    if value("symbolic-ref", "HEAD") != BRANCH:
        raise RuntimeError("Expected main to be the checked-out branch")
    if value("rev-parse", BRANCH) != SOURCE:
        raise RuntimeError("main changed: refusing to split a different commit")
    if value("show", "-s", "--format=%P", SOURCE) != BASE:
        raise RuntimeError("Source must have exactly the expected published parent")
    source_tree = value("rev-parse", SOURCE + "^{tree}")
    existing_backup = value("for-each-ref", "--format=%(objectname)", BACKUP)
    if existing_backup and existing_backup != SOURCE:
        raise RuntimeError("Backup ref already points elsewhere; refusing to replace it")

    # Git's NUL-delimited raw diff preserves binary-safe paths, Unicode, tabs,
    # newlines, deletions, modes, and type changes; disabling renames simplifies it.
    tokens = git("diff-tree", "--no-commit-id", "--raw", "-z", "-r",
                 "--no-renames", "--no-abbrev", BASE, SOURCE).split(b"\0")
    entries = []
    for i in range(0, len(tokens) - 1, 2):
        old_mode, mode, old_oid, oid, status = tokens[i].split()
        entries.append({"mode": mode, "oid": oid, "path": tokens[i + 1],
                        "status": status.decode("ascii")})

    reachable = git("rev-list", "--objects", "--no-object-names", SOURCE, "^" + BASE)
    sizes = {}
    for line in git("cat-file", "--batch-check=%(objectname) %(objecttype) %(objectsize)",
                    data=reachable).splitlines():
        oid, kind, size = line.split()
        if kind == b"blob":
            sizes[oid] = int(size)

    # Report new LFS pointers separately from ordinary Git bytes. The caller must
    # finish/verify LFS upload before claiming the published project is complete.
    small = [oid for oid, size in sizes.items() if size <= 1024]
    lfs = {}
    if small:
        stream = io.BytesIO(git("cat-file", "--batch", data=b"\n".join(small) + b"\n"))
        for expected in small:
            oid, kind, length = stream.readline().split()
            content = stream.read(int(length))
            if stream.read(1) != b"\n" or oid != expected or kind != b"blob":
                raise RuntimeError("Unexpected cat-file batch response")
            if content.startswith(b"version https://git-lfs.github.com/spec/v1\n"):
                fields = dict(line.split(b" ", 1) for line in content.splitlines())
                lfs[oid] = {"oid": fields[b"oid"].decode("ascii"),
                            "size": int(fields[b"size"])}

    batches, current, seen, total = [], [], set(), 0
    for entry in entries:
        cost = sizes.get(entry["oid"], 0) if entry["oid"] not in seen else 0
        if cost > LIMIT:
            raise RuntimeError("A single new blob exceeds the conservative batch limit")
        if current and total + cost > LIMIT:
            batches.append((current, total))
            current, total = [], 0
        current.append(entry)
        total += cost
        seen.add(entry["oid"])
    if current:
        batches.append((current, total))
    if not batches:
        raise RuntimeError("No changes to split")

    def display(path):
        return path.decode("utf-8", "surrogateescape")

    report = {
        "state": "planned", "repo": str(repo), "base": BASE, "source": SOURCE,
        "source_tree": source_tree, "branch": BRANCH, "backup_ref": BACKUP,
        "raw_new_blob_limit_bytes": LIMIT, "total_new_blob_bytes": sum(sizes.values()),
        "new_lfs_object_count": len({x["oid"] for x in lfs.values()}),
        "new_lfs_object_bytes": sum({x["oid"]: x["size"] for x in lfs.values()}.values()),
        "push_policy": "Sequential fast-forward pushes; normal LFS pre-push hook remains enabled.",
        "batches": [],
    }
    for number, (batch, size) in enumerate(batches, 1):
        report["batches"].append({
            "number": number, "entries": len(batch), "new_blob_bytes": size,
            "first_path": display(batch[0]["path"]), "last_path": display(batch[-1]["path"]),
            "deletions": sum(e["status"] == "D" for e in batch),
            "lfs_pointers": [{"path": display(e["path"]), **lfs[e["oid"]]}
                             for e in batch if e["oid"] in lfs],
        })

    def save_report():
        payload = json.dumps(report, indent=2, ensure_ascii=True) + "\n"
        if args.report:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(payload, encoding="utf-8")
        return payload

    if not args.apply:
        print(save_report(), end="")
        return

    index_path = Path(value("rev-parse", "--git-path", "index"))
    if not index_path.is_absolute():
        index_path = repo / index_path
    def index_hash():
        return hashlib.sha256(index_path.read_bytes()).hexdigest() if index_path.exists() else None
    original_index = index_hash()

    # Explicitly inherit the automation identity and dates of our own source
    # commit, rather than relying on user/global Git configuration.
    identity = git("show", "-s", "--format=%an%x00%ae%x00%aI%x00%cn%x00%ce%x00%cI", SOURCE).strip().split(b"\0")
    if len(identity) != 6:
        raise RuntimeError("Cannot read original commit identity")
    commit_env = os.environ.copy()
    for key, item in zip(("GIT_AUTHOR_NAME", "GIT_AUTHOR_EMAIL", "GIT_AUTHOR_DATE",
                          "GIT_COMMITTER_NAME", "GIT_COMMITTER_EMAIL", "GIT_COMMITTER_DATE"), identity):
        commit_env[key] = item.decode("utf-8")
    report["state"] = "creating_commits"
    report["original_index_sha256"] = original_index
    save_report()

    try:
        with tempfile.TemporaryDirectory(prefix="cgdd-publish-index-") as scratch:
            commit_env["GIT_INDEX_FILE"] = str(Path(scratch) / "index")
            git("read-tree", BASE, env=commit_env)
            parent = BASE
            for item, (batch, size) in zip(report["batches"], batches):
                records = b"".join(e["mode"] + b" " + e["oid"] + b"\t" + e["path"] + b"\0" for e in batch)
                git("update-index", "-z", "--index-info", data=records, env=commit_env)
                tree = git("write-tree", env=commit_env).decode("ascii").strip()
                message = ("Sync Starframe gameplay and assets (publish batch " +
                           str(item["number"]) + "/" + str(len(batches)) + ")\n\n" +
                           "Lossless split of unpublished commit " + SOURCE + ".\n").encode("utf-8")
                commit = git("commit-tree", tree, "-p", parent, data=message, env=commit_env).decode("ascii").strip()
                item.update({"commit": commit, "parent": parent, "tree": tree,
                             "push_command": "git push origin " + commit + ":" + BRANCH})
                parent = commit
                save_report()
            if tree != source_tree:
                raise RuntimeError("Final tree differs from original; no branch ref was changed")
            if index_hash() != original_index:
                raise RuntimeError("Real index changed concurrently; no branch ref was changed")
            report["state"] = "commits_ready"
            report["final_commit"] = parent
            save_report()
            # Both ref operations are one transaction, with expected-old guards.
            backup_op = ("verify " if existing_backup else "create ") + BACKUP + " " + SOURCE
            transaction = ("start\n" + backup_op + "\nupdate " + BRANCH + " " + parent +
                           " " + SOURCE + "\nprepare\ncommit\n").encode("ascii")
            git("update-ref", "--stdin", data=transaction)
            report["state"] = "applied"
            report["final_tree_verified"] = True
            report["real_index_unchanged"] = index_hash() == original_index
            save_report()
    except Exception as error:
        report["error"] = str(error)
        save_report()
        raise
    print(json.dumps({key: report[key] for key in ("state", "final_commit", "source_tree",
                      "final_tree_verified", "real_index_unchanged")}, indent=2))


if __name__ == "__main__":
    main()
