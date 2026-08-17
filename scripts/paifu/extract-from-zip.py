#!/usr/bin/env python3
"""从年度牌谱包里按 id 提取 mjai 牌谱，摆成 `JANPO_PAIFU_DIR` 认的目录形状（票 62）。

    python3 scripts/paifu/extract-from-zip.py <zip> <outdir> <ids文件>

产出 `<outdir>/mjai/<id>.mjson`。用在两处：

1. **oracle 先筛后取**：免 oracle 大扫描筛出可疑场（`summarize-scan.py` 的 suspects.txt
   与各类样本）之后，把它们提出来，`fetch-corpus.py <outdir> --ids <ids文件> --oracle`
   补上天凤 JSON，再用 `paifu-scan.fsx` 带 oracle 重扫这一小撮；
2. **留证据**：差异场的原始牌谱证据本体在压缩包里，报告要引用哪场就提哪场。

已存在的文件跳过（幂等）。
"""

import os
import sys
import zipfile


def main():
    if len(sys.argv) != 4:
        sys.exit(__doc__)

    zip_path, outdir, ids_path = sys.argv[1:]
    with open(ids_path, encoding="utf-8") as handle:
        wanted = {line.strip() for line in handle if line.strip()}

    mjai_dir = os.path.join(outdir, "mjai")
    os.makedirs(mjai_dir, exist_ok=True)

    written = 0
    with zipfile.ZipFile(zip_path) as archive:
        for name in archive.namelist():
            log_id = os.path.basename(name)[: -len(".mjson")] if name.endswith(".mjson") else None
            if log_id not in wanted:
                continue
            target = os.path.join(mjai_dir, log_id + ".mjson")
            if not os.path.exists(target):
                with archive.open(name) as source, open(target, "wb") as sink:
                    sink.write(source.read())
                written += 1
            wanted.discard(log_id)

    print(f"提取 {written} 场 → {mjai_dir}")
    if wanted:
        print(f"包里找不到这些 id：{' '.join(sorted(wanted))}", file=sys.stderr)


if __name__ == "__main__":
    main()
