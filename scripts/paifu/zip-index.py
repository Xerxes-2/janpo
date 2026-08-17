#!/usr/bin/env python3
"""列出年度牌谱包里全部 .mjson 成员的读取坐标，给 `paifu-scan-zip.fsx` 流式读用（票 62）。

    python3 scripts/paifu/zip-index.py <zip> > /tmp/janpo-scan/index-<年份>.tsv

每行：`成员名 <tab> 本地头偏移 <tab> 压缩后字节数 <tab> 压缩方法`，按成员名排序——
排序在这里做一次，扫描侧各分片按行号取模就是确定的分片，断点续跑靠它。

为什么不用 .NET 的 `ZipArchive` 直接开：整年包约 250 万个成员，`ZipArchive`
把中央目录整个实体化成对象（实测约每成员几百字节），16 个分片进程就是十几 GB 内存。
标准库 `zipfile` 解析一遍（含 Zip64——成员数超过 65,535 时必然用到），
把坐标吐成文本，扫描侧就只需要 `FileStream.Seek` + `DeflateStream`，每进程几十 MB。

索引放 /tmp 即可：几秒钟就能重新生成，不算语料的一部分（磁盘净增长判据不含它）。
"""

import sys
import zipfile


def main():
    if len(sys.argv) != 2:
        sys.exit(__doc__)

    with zipfile.ZipFile(sys.argv[1]) as archive:
        infos = [info for info in archive.infolist() if info.filename.endswith(".mjson")]
        for info in sorted(infos, key=lambda info: info.filename):
            print(f"{info.filename}\t{info.header_offset}\t{info.compress_size}\t{info.compress_type}")

    print(f"{len(infos)} 个成员", file=sys.stderr)


if __name__ == "__main__":
    main()
