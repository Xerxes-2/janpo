#!/usr/bin/env python3
"""取一批天凤鳳凰卓牌谱，供真牌谱对拍（票 13 / 16 / 57）用。

    python3 scripts/paifu/fetch-corpus.py <outdir> [局数] [步长] [--oracle]
    python3 scripts/paifu/fetch-corpus.py <outdir> --all                 # 整包 12,188 场，1 次请求
    python3 scripts/paifu/fetch-corpus.py <outdir> --ids <文件> --oracle  # 只补这些场的 oracle

产出 `<outdir>/mjai/<id>.mjson`（mjai 事件流），带 `--oracle` 时另产出
`<outdir>/tenhou/<id>.json`（天凤官方 JSON，役 / 符 / 番 / 点数的 oracle）。
跑对拍时把 `JANPO_PAIFU_DIR` 指向 `<outdir>` 即可，**不必改代码**：

    JANPO_PAIFU_DIR=$PWD/data/paifu dotnet test -c Release --filter PaifuDifferentialTests

来源：`NikkeTryHard/tenhou-to-mjai` release v2.0.0（数据 CC BY 4.0，全部为天凤鳳凰卓四麻半庄）。

**下行量与礼貌**（RUNBOOK：单次下载 ≤ ~200 MB，单线程、请求之间有间隔）：

- 抽样模式用 HTTP Range 只取中央目录与抽样成员，58MB 的包实际下行几 MB；
  **抽样超过几百场就该换 `--all`**——一次 55MB 的整包请求比几千次 Range 请求礼貌得多（票 57）。
- `--all` 把整包缓存在 `<outdir>/2026.zip`，重跑不再下行；解包出 12,188 场、约 570 MB。
- 天凤那侧每局一次请求、间隔 1.5s（`--oracle`），**只对要当 oracle 用的那批开**：
  12,188 场全取要 5 小时，那是打扰别人。
"""

import os
import struct
import sys
import time
import urllib.request
import zlib

ZIP_URL = "https://github.com/NikkeTryHard/tenhou-to-mjai/releases/download/v2.0.0/2026.zip"
ORACLE_URL = "https://tenhou.net/5/mjlog2json.cgi?%s"
AGENT = "janpo-paifu/1.0"


def get(url, start, end):
    request = urllib.request.Request(
        url, headers={"Range": f"bytes={start}-{end}", "User-Agent": AGENT}
    )
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()


def size(url):
    request = urllib.request.Request(url, method="HEAD", headers={"User-Agent": AGENT})
    with urllib.request.urlopen(request, timeout=60) as response:
        return int(response.headers["Content-Length"])


def central_directory(url):
    total = size(url)
    tail = get(url, max(0, total - 65_557), total - 1)
    end = tail.rfind(b"PK\x05\x06")
    _, _, _, _, count, cd_size, cd_offset, _ = struct.unpack("<IHHHHIIH", tail[end : end + 22])
    zip64 = tail.rfind(b"PK\x06\x06")
    if zip64 >= 0 and (cd_offset == 0xFFFFFFFF or count == 0xFFFF):
        fields = struct.unpack("<IQHHIIQQQQ", tail[zip64 : zip64 + 56])
        count, cd_size, cd_offset = fields[7], fields[8], fields[9]
    return get(url, cd_offset, cd_offset + cd_size - 1)


def members(url):
    directory = central_directory(url)
    entries, cursor = [], 0
    while cursor < len(directory) and directory[cursor : cursor + 4] == b"PK\x01\x02":
        header = struct.unpack("<IHHHHHHIIIHHHHHII", directory[cursor : cursor + 46])
        method, csize, name_len, extra_len, comment_len, local = (
            header[4],
            header[8],
            header[10],
            header[11],
            header[12],
            header[16],
        )
        name = directory[cursor + 46 : cursor + 46 + name_len].decode("utf-8", "replace")
        entries.append((name, method, csize, local))
        cursor += 46 + name_len + extra_len + comment_len
    return sorted(entry for entry in entries if entry[0].endswith(".mjson"))


def fetch_member(url, method, csize, local):
    header = get(url, local, local + 29)
    fields = struct.unpack("<IHHHHHIIIHH", header[:30])
    name_len, extra_len = fields[9], fields[10]
    body = get(url, local + 30 + name_len + extra_len, local + 30 + name_len + extra_len + csize - 1)
    return zlib.decompressobj(-15).decompress(body) if method == 8 else body


def fetch_whole(url, path):
    """整包下载（1 次请求），缓存到本地。抽样几千场时比几千次 Range 请求礼貌得多。"""
    if os.path.exists(path):
        print(f"整包已在 {path}（{os.path.getsize(path) / 1e6:.1f} MB），不重复下载")
        return path
    request = urllib.request.Request(url, headers={"User-Agent": AGENT})
    with urllib.request.urlopen(request, timeout=600) as response, open(path, "wb") as handle:
        while True:
            chunk = response.read(1 << 20)
            if not chunk:
                break
            handle.write(chunk)
    print(f"整包下载完成 → {path}（{os.path.getsize(path) / 1e6:.1f} MB）")
    return path


def extract_all(zip_path, mjai_dir):
    """把缓存的整包解到 `mjai/`。用标准库 zipfile，不再有一次网络请求。"""
    import zipfile

    written = 0
    with zipfile.ZipFile(zip_path) as archive:
        names = [name for name in archive.namelist() if name.endswith(".mjson")]
        for name in names:
            target = os.path.join(mjai_dir, os.path.basename(name))
            if os.path.exists(target):
                continue
            with archive.open(name) as source, open(target, "wb") as handle:
                handle.write(source.read())
            written += 1
    print(f"解出 {len(names)} 场（新写 {written} 场）→ {mjai_dir}")
    return names


def fetch_oracle(log_id):
    request = urllib.request.Request(
        ORACLE_URL % log_id,
        headers={"Referer": "https://tenhou.net/", "User-Agent": AGENT},
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read()


def option(flag, fallback=None):
    """读 `--flag 值` 形式的选项。"""
    argv = sys.argv[1:]
    return argv[argv.index(flag) + 1] if flag in argv and len(argv) > argv.index(flag) + 1 else fallback


def fetch_oracles(log_ids, tenhou_dir):
    """逐个补 oracle，**已有的跳过**（重跑不重复打扰天凤），每次请求间隔 1.5s。"""
    os.makedirs(tenhou_dir, exist_ok=True)
    missing = [
        log_id for log_id in log_ids if not os.path.exists(os.path.join(tenhou_dir, log_id + ".json"))
    ]
    print(f"要补 {len(missing)} 场 oracle（间隔 1.5s，约 {len(missing) * 1.6 / 60:.0f} 分钟）")
    for index, log_id in enumerate(missing):
        with open(os.path.join(tenhou_dir, log_id + ".json"), "wb") as handle:
            handle.write(fetch_oracle(log_id))
        time.sleep(1.5)
        if (index + 1) % 50 == 0:
            print(f"  oracle {index + 1}/{len(missing)}", flush=True)


def main():
    flags = [arg for arg in sys.argv[1:] if arg.startswith("--")]
    args = [arg for arg in sys.argv[1:] if not arg.startswith("--")]
    ids_file = option("--ids")
    if ids_file:
        args = [arg for arg in args if arg != ids_file]
    want_oracle = "--oracle" in flags
    want_all = "--all" in flags
    outdir = args[0] if args else "data/paifu"
    count = int(args[1]) if len(args) > 1 else 60
    stride = int(args[2]) if len(args) > 2 else 97

    mjai_dir = os.path.join(outdir, "mjai")
    tenhou_dir = os.path.join(outdir, "tenhou")
    os.makedirs(mjai_dir, exist_ok=True)

    # `--ids`：只补这些场（mjai 从已解包的目录里取，oracle 现拉）。扩样本挑完覆盖再补 oracle 用。
    if ids_file:
        with open(ids_file, encoding="utf-8") as handle:
            log_ids = [line.strip() for line in handle if line.strip()]
        if want_oracle:
            fetch_oracles(log_ids, tenhou_dir)
        print("完成")
        return

    # `--all`：整包一次请求，之后全在本地解包。**几千场时用它**，别拿 Range 打几千次。
    if want_all:
        names = extract_all(fetch_whole(ZIP_URL, os.path.join(outdir, "2026.zip")), mjai_dir)
        if want_oracle:
            fetch_oracles([os.path.basename(name)[: -len(".mjson")] for name in names], tenhou_dir)
        print("完成")
        return

    picked = members(ZIP_URL)[::stride][:count]
    print(f"取 {len(picked)} 局 → {mjai_dir}")

    for index, (name, method, csize, local) in enumerate(picked):
        log_id = os.path.basename(name)[: -len(".mjson")]
        target = os.path.join(mjai_dir, log_id + ".mjson")
        if not os.path.exists(target):
            with open(target, "wb") as handle:
                handle.write(fetch_member(ZIP_URL, method, csize, local))
        if want_oracle:
            oracle = os.path.join(tenhou_dir, log_id + ".json")
            if not os.path.exists(oracle):
                os.makedirs(tenhou_dir, exist_ok=True)
                with open(oracle, "wb") as handle:
                    handle.write(fetch_oracle(log_id))
                time.sleep(1.5)
        if (index + 1) % 20 == 0:
            print(f"  {index + 1}/{len(picked)}")

    print("完成")


if __name__ == "__main__":
    main()
