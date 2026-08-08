#!/usr/bin/env python3
"""
clean_codex_rollout.py — 清理 Codex CLI rollout 文件中的加密推理内容

用法：
  python3 clean_codex_rollout.py                      # 清理 ~/.codex/ 下所有 rollout 文件
  python3 clean_codex_rollout.py path/to/file.jsonl   # 清理指定文件
  python3 clean_codex_rollout.py --dry-run            # 预览，不实际修改
"""

import sys
import os
import json
import shutil
import glob
from pathlib import Path


def is_encrypted_reasoning(obj: dict) -> bool:
    """判断是否为需要删除的 reasoning 行（含 encrypted_content）"""
    if obj.get("type") != "response_item":
        return False
    payload = obj.get("payload", {})
    if payload.get("type") != "reasoning":
        return False
    # 只删除含 encrypted_content 的 reasoning，保留纯 summary 的
    return "encrypted_content" in json.dumps(payload)


def clean_file(path: str, dry_run: bool = False) -> tuple[int, int]:
    """
    清理单个 rollout 文件。
    返回 (总行数, 删除行数)
    """
    path = Path(path)
    if not path.exists():
        print(f"  [跳过] 文件不存在: {path}")
        return 0, 0

    lines_kept = []
    lines_dropped = 0
    total = 0

    with open(path, "r", encoding="utf-8") as f:
        for raw in f:
            raw_stripped = raw.strip()
            if not raw_stripped:
                lines_kept.append(raw)
                continue
            total += 1
            try:
                obj = json.loads(raw_stripped)
            except json.JSONDecodeError:
                # 解析失败的行（含 NUL 或乱码）也一并丢弃
                lines_dropped += 1
                print(f"  [丢弃] 无效 JSON 行")
                continue

            if is_encrypted_reasoning(obj):
                lines_dropped += 1
            else:
                lines_kept.append(raw)

    if dry_run:
        print(f"  [预览] {path.name}: 共 {total} 行，将删除 {lines_dropped} 行（{lines_kept.__len__()} 行保留）")
        return total, lines_dropped

    if lines_dropped == 0:
        print(f"  [干净] {path.name}: 无需清理")
        return total, 0

    # 备份原文件
    backup = path.with_suffix(".jsonl.bak")
    shutil.copy2(path, backup)
    print(f"  [备份] → {backup.name}")

    # 写回清理后内容
    with open(path, "w", encoding="utf-8") as f:
        f.writelines(lines_kept)

    print(f"  [完成] {path.name}: 删除 {lines_dropped} 条 reasoning，保留 {total - lines_dropped} 行")
    return total, lines_dropped


def find_rollout_files(directory: str) -> list[str]:
    pattern = os.path.join(directory, "rollout-*.jsonl")
    return sorted(glob.glob(pattern))


def main():
    args = sys.argv[1:]
    dry_run = "--dry-run" in args
    args = [a for a in args if a != "--dry-run"]

    if args:
        # 指定文件
        targets = args
    else:
        # 自动查找 ~/.codex/
        codex_dir = os.path.expanduser("~/.codex")
        targets = find_rollout_files(codex_dir)
        if not targets:
            print(f"未在 {codex_dir} 找到任何 rollout-*.jsonl 文件")
            print("用法: python3 clean_codex_rollout.py [文件路径] [--dry-run]")
            sys.exit(0)
        print(f"在 {codex_dir} 找到 {len(targets)} 个 rollout 文件\n")

    if dry_run:
        print("=== 预览模式（不修改文件）===\n")

    total_dropped = 0
    for target in targets:
        _, dropped = clean_file(target, dry_run=dry_run)
        total_dropped += dropped

    print()
    if dry_run:
        print(f"预览完成，共计将删除 {total_dropped} 条 reasoning 记录")
    else:
        print(f"清理完成，共计删除 {total_dropped} 条 reasoning 记录")
        if total_dropped > 0:
            print("备份文件保留为 .jsonl.bak，确认无误后可手动删除")


if __name__ == "__main__":
    main()
