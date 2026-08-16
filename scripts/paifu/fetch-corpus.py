#!/usr/bin/env python3
"""取一批天凤鳳凰卓牌谱，供 13 票的对拍扩样本用。

    python3 scripts/paifu/fetch-corpus.py <outdir> [局数] [步长] [--oracle]

产出 `<outdir>/mjai/<id>.mjson`（mjai 事件流），带 `--oracle` 时另产出
`<outdir>/tenhou/<id>.json`（天凤官方 JSON，役 / 符 / 番 / 点数的 oracle）。
跑对拍时把 `JANPO_PAIFU_DIR` 指向 `<outdir>` 即可，**不必改代码**：

    JANPO_PAIFU_DIR=$PWD/data/paifu dotnet test -c Release --filter PaifuDifferentialTests

来源：`NikkeTryHard/tenhou-to-mjai` release v2.0.0（数据 CC BY 4.0，全部为天凤鳳凰卓四麻半庄）。
zip 没有整包下载的必要——这里用 HTTP Range 只取中央目录与抽样成员，58MB 的包实际下行几 MB。

**资源纪律**（RUNBOOK）：单线程、请求之间有间隔；天凤那侧每局一次请求、间隔 1.5s。
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


def fetch_oracle(log_id):
    request = urllib.request.Request(
        ORACLE_URL % log_id,
        headers={"Referer": "https://tenhou.net/", "User-Agent": AGENT},
    )
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read()


def main():
    args = [arg for arg in sys.argv[1:] if not arg.startswith("--")]
    want_oracle = "--oracle" in sys.argv[1:]
    outdir = args[0] if args else "data/paifu"
    count = int(args[1]) if len(args) > 1 else 60
    stride = int(args[2]) if len(args) > 2 else 97

    mjai_dir = os.path.join(outdir, "mjai")
    tenhou_dir = os.path.join(outdir, "tenhou")
    os.makedirs(mjai_dir, exist_ok=True)
    if want_oracle:
        os.makedirs(tenhou_dir, exist_ok=True)

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
                with open(oracle, "wb") as handle:
                    handle.write(fetch_oracle(log_id))
                time.sleep(1.5)
        if (index + 1) % 20 == 0:
            print(f"  {index + 1}/{len(picked)}")

    print("完成")


if __name__ == "__main__":
    main()
