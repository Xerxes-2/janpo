# 13 票前置 —— 真实牌谱数据侧勘察

**STATUS: done**（本报告不实现任何票；产出＝数据 + 固件 + 缺口清单）

一句话结论：**首选数据源拿到了，而且够用；但它的 `hora` 与 `ryukyoku` 是「瘦身版」——役种、符、番、
和了点、流局形态全被上游丢掉了**。13 票要对拍的正好是这些，所以本报告额外配了一份天凤官方 JSON
作 oracle，两者按局一一对齐（177 局零误差）。18 局固件已落到 `tests/fixtures/paifu/`。

---

## 一、数据源与取法（可复现）

### 命中阶梯第 1 级：`NikkeTryHard/tenhou-to-mjai`

- Release `v2.0.0` 按年打包：`2009.zip`(32MB) … `2026.zip`(58MB)，共 ~12GB / 250 万局。
- 全部是**天凤鳳凰卓四麻半庄**（上游 README 明说不含三麻与东风战），文件名就是天凤牌谱 id。
- 许可：数据 CC BY 4.0（`LICENSE-DATA`），代码 Apache-2.0。
- 注意：`2009.zip` 里的成员是 gzip（要 `gunzip`），`2026.zip` 里的成员**已经是明文 NDJSON**——
  上游 README 只说了前者，别照着 README 无脑 gunzip。

**没有整包下载。** 用 HTTP Range 只取 zip 的中央目录 + 抽样成员，58MB 的包实际下行 ~3.5MB。
脚本（本报告自带，`data/paifu/` 被 gitignore 了）：

```python
# data/paifu/fetch_sample.py —— 用法: python3 fetch_sample.py <zip-url> <outdir> <count> <stride>
# 默认: 2026.zip / raw / 60 局 / 每 97 个成员取 1 个（打散到全月）
import io, os, struct, sys, urllib.request, zlib
URL = sys.argv[1] if len(sys.argv) > 1 else "https://github.com/NikkeTryHard/tenhou-to-mjai/releases/download/v2.0.0/2026.zip"
OUT = sys.argv[2] if len(sys.argv) > 2 else "raw"
COUNT = int(sys.argv[3]) if len(sys.argv) > 3 else 60
STRIDE = int(sys.argv[4]) if len(sys.argv) > 4 else 97

def get(url, start, end):
    req = urllib.request.Request(url, headers={"Range": f"bytes={start}-{end}", "User-Agent": "janpo-prep/1.0"})
    with urllib.request.urlopen(req, timeout=120) as r: return r.read()

def size(url):
    req = urllib.request.Request(url, method="HEAD", headers={"User-Agent": "janpo-prep/1.0"})
    with urllib.request.urlopen(req, timeout=60) as r: return int(r.headers["Content-Length"])

total = size(URL)
tail = get(URL, max(0, total - 65_557), total - 1)
i = tail.rfind(b"PK\x05\x06")
_, _, _, _, cd_count, cd_size, cd_off, _ = struct.unpack("<IHHHHIIH", tail[i:i+22])
j = tail.rfind(b"PK\x06\x06")
if j >= 0 and (cd_off == 0xFFFFFFFF or cd_count == 0xFFFF):
    f = struct.unpack("<IQHHIIQQQQ", tail[j:j+56]); cd_count, cd_size, cd_off = f[7], f[8], f[9]
cd = get(URL, cd_off, cd_off + cd_size - 1)
entries, p = [], 0
while p < len(cd) and cd[p:p+4] == b"PK\x01\x02":
    (_, _, _, _, method, _, _, crc, csize, usize, nlen, elen, clen, _, _, _, lho) = struct.unpack("<IHHHHHHIIIHHHHHII", cd[p:p+46])
    entries.append((cd[p+46:p+46+nlen].decode("utf-8", "replace"), method, csize, usize, lho))
    p += 46 + nlen + elen + clen
members = sorted(e for e in entries if e[0].endswith(".mjson"))
os.makedirs(OUT, exist_ok=True)
for name, method, csize, usize, lho in members[::STRIDE][:COUNT]:
    hdr = get(URL, lho, lho + 29)
    _, _, _, _, _, _, _, _, _, nlen, elen = struct.unpack("<IHHHHHIIIHH", hdr[:30])
    off = lho + 30 + nlen + elen
    raw = get(URL, off, off + csize - 1)
    if method == 8: raw = zlib.decompressobj(-15).decompress(raw)
    open(os.path.join(OUT, os.path.basename(name)), "wb").write(raw)
```

### 额外配的 oracle：天凤官方 JSON

上游 mjai 流丢掉了役 / 符 / 番 / 和了点 / 流局形态（见第四节 A2）。但**牌谱 id 就在文件名里**，
天凤官方的 JSON 导出接口还活着，同一局能取回全部期望值：

```python
# data/paifu/fetch_oracle.py —— 每局一次请求，间隔 1.5s，~9KB/局
url = f"https://tenhou.net/5/mjlog2json.cgi?{log_id}"          # Referer: https://tenhou.net/
```

返回形如 `{"ver":2.3,"ref":"<id>","rule":{"disp":"鳳南喰赤","aka51":1,...},"log":[ <每局> ]}`，
每局末尾是结果：

```
["和了", [-1300,-700,-700,2700], [3, 3, 3, "40符2飜700-1300点", "役牌 白(1飜)", "混全帯幺九(1飜)"]]
["流局", [1500,-1500,1500,-1500]]      ["九種九牌"]     ["全員聴牌"]     ["全員不聴"]
```

→ **役种集合、符、番、点数、流局形态、包牌家全有**，正是 13 票验收要比的东西。

### 阶梯降级路线的实测结论（不必再试）

| 路线 | 实测 | 结论 |
|---|---|---|
| `tenhou.net/0/log/?id=<id>`（原始 mjlog XML） | **404**（带不带 Referer 都是） | 现在要账号态，夜间不碰 |
| `tenhou.net/sc/raw/list.cgi` + `dat/sccYYYYMMDDHH.html.gz` | 200，能列出近几日的牌谱 id | 只给 id，取不到牌谱本体，且只有近期 |
| `tenhou.net/5/mjlog2json.cgi?<id>` | **200**，2026-01 的老 id 也能取 | 已采用为 oracle |
| `fstqwq/mjlog2mjai`（次选，自己转） | 读过 `parse.py`：它产出的 `hora` 同样只有 `actor/target/deltas/ura_markers` | **降级也解决不了字段缺口**，而且还多一个「取不到 mjlog」的死结 |

> 因此 **没有降级**：首选源 + 天凤 JSON oracle 的组合，比阶梯里任何一级都完整。

---

## 二、样本规模

| 批次 | 局(game) | 事件行 | 局(kyoku) | 用途 |
|---|---|---|---|---|
| `data/paifu/raw/`（2026.zip，2026-01） | 60 | 66,264 | 652 | 主样本，字段分析与固件来源 |
| `data/paifu/oracle/`（天凤 JSON） | 60 | — | 652 | 役/符/点数/流局形态 oracle |
| `data/paifu/raw2009/`（2009.zip） | 8 | 11,574 | 105 | 只为验证「老年份 schema 是否不同」 |
| `tests/fixtures/paifu/`（已提交） | 18 | — | 177 | 固件子集，987,413 字节 |

`data/paifu/` 已加进 `.gitignore`（不入版本控制）。分析脚本
（`analyze.py` / `features.py` / `pick3.py` / `seq.py` / `scorecheck.py`）也在那里，随时可重跑。

---

## 三、事件 type 与字段全表（60 局 / 66,264 行）

| type | 次数 | 字段与取值形态 |
|---|---|---|
| `dahai` | 31402 | `actor` 0-3；`pai` 37 种记法；`tsumogiri` bool（true 11864 / false 19538） |
| `tsumo` | 30449 | `actor` 0-3；`pai` |
| `pon` | 749 | `actor` / `target` 0-3；`pai`；`consumed` **长度 2** |
| `start_kyoku` | 652 | `bakaze` `"E"`×355 `"S"`×287 `"W"`×10；`kyoku` 1-4；`honba` 0-5；`kyotaku` 0-3；`oya` 0-3；`dora_marker`；`scores` 长 4；`tehais` 长 4×13 |
| `end_kyoku` | 652 | 无字段 |
| `hora` | 556 | `actor` / `target` 0-3；`deltas` 长 4；`ura_markers` 长 0-3。**没有** `pai` / `yakus` / `fu` / `fan` / `hora_points` / `scores` |
| `chi` | 496 | `actor` / `target`；`pai`；`consumed` **长度 2** |
| `reach` | 491 | 只有 `actor` |
| `reach_accepted` | 479 | 只有 `actor`（**没有** `deltas` / `scores`，与上游 README 的描述不符） |
| `ryukyoku` | 99 | 只有 `deltas` 长 4。**没有** `reason` / `tenpais` / `scores` / `tehais` |
| `start_game` | 60 | `names` 长 4；`kyoku_first` 恒 0；`aka_flag` 恒 true |
| `end_game` | 60 | 无字段 |
| `dora` | 59 | 只有 `dora_marker`（杠宝牌翻牌） |
| `kakan` | 30 | `actor`；`pai`；`consumed` **长度 3**（无 `target`） |
| `ankan` | 29 | `actor`；`consumed` **长度 4**（无 `pai` / `target`） |
| `daiminkan` | 1 | `actor` / `target`；`pai`；`consumed` **长度 3** |

**牌记法（37 种，与 ADR-0001 的差异是本报告最要命的一条）**：
数牌 `1m-9m` `1p-9p` `1s-9s` 与赤 5 `5mr` `5pr` `5sr` 与我们一致；
**字牌是字母**：`E`(1z 东) `S`(2z 南) `W`(3z 西) `N`(4z 北) `P`(5z 白) `F`(6z 發) `C`(7z 中)。
`start_kyoku.bakaze` 同样是 `"E"` / `"S"` / `"W"` / `"N"`。

**没有出现**（确认过两批样本）：`nukidora`（三麻拔北）、`kan`（统称）、`error`、`tenpai`、`ryukyoku.reason`。
所有 `tehais` 都是 4 家 × 13 张 → **样本零三麻污染**；载入器加一句 `tehais.Length = 4` 断言即可。

### 顺序与语义（replay driver 会踩的坑）

| 观察 | 次数 | 含义 |
|---|---|---|
| `tsumo → reach → dahai` | 491 | 立直宣言事件在打牌**之前** |
| `dahai → reach_accepted → tsumo/chi/pon` | 479 | 立直成立在打牌**之后**；491−479=**12 次立直宣言牌被荣和，没有 `reach_accepted`** |
| `ankan → dora → tsumo` | 29 | 暗杠：**立即**翻新宝牌，再摸岭上 |
| `tsumo → dora → dahai` | 30 | 加杠 / 大明杠：先摸岭上，**打牌前**翻（天凤时机） |
| `dahai → hora → hora` | 3 | **双响**（天凤双响あり，与 S-A 表一致） |
| `tsumo → ryukyoku` | 6 | 前驱是摸牌 ⟹ **九種九牌**（与 oracle 的 6 次完全吻合） |
| `dahai → ryukyoku` | 93 | 前驱是打牌 ⟹ 荒牌流局（含全員聴牌 3 / 全員不聴 2） |
| `hora.deltas` 求和 ∈ {0,1000,2000,3000,4000} | — | deltas **含供托与本场**，与我们 `Hora.Deltas` 的注释语义一致 ✓ |

**点数可以只用 mjai 流重建**（60 局 592 次局间转移，零误差）：

```
下一个 start_kyoku.scores = 上一个 scores − 1000 × (reach_accepted 次数, 按 actor) + Σ(hora|ryukyoku).deltas
```

**mjai 流与天凤 JSON 按局一一对齐**（18 局固件的 177 局全部验过）：第 k 个 `start_kyoku` ↔
`log[k]`，`(bakaze, kyoku, honba, kyotaku)` 与 deltas 全等（九種九牌那局天凤侧不给 deltas，除外）。

---

## 四、缺口清单

### 第一档 —— 13 票必须补（不补一行都跑不起来）

- **A1 字牌记法映射。** 样本用 `E/S/W/N/P/F/C`，`Tile.parse` 只认 `1z`-`7z`，遇到 `"E"` 直接
  `MalformedNotation`。`bakaze` 同理（`"E"` vs `Kaze.toMjai` 的 `"1z"`）。
  建议：映射写在 13 票自己的**牌谱适配器**里（`Paifu.fs` / 测试侧），**不要**放宽 `Tile.parse`——
  ADR-0001 与 01 票的决策都明确「数据层只有一种记法」，宽松解析会让第二种记法渗回事件流。
- **A2 `Event.decoder` 读不了这个牌谱。** `hora` 缺 `pai`/`fu`/`fan`/`hora_points`/`scores`，
  `ryukyoku` 缺 `reason`/`tenpais`/`scores`，全是 `Required.Field` → 解码必失败。
  建议：13 票另立一个**只读的 `PaifuEvent`**（宽松、字段可选、含 `ura_markers`），
  与引擎产出的 `Event` 分开；不要为了读牌谱把 `Event` 的字段改成可选（那会让引擎自己产出的
  事件也失去「必然完整」这条不变量）。
- **A3 引擎还没有的 8 种事件 case**：`pon` `chi` `ankan` `kakan` `daiminkan` `reach`
  `reach_accepted` `dora`。它们分别是 10（碰/吃）、11（三种杠 + `dora`）、09（立直两事件）的产出。
  **13 的 blocked-by 里写了 08/09/11，但没写 10**——样本里 `pon` 749 次、`chi` 496 次，
  是最高频的副露，**没有 10 票就没法重放绝大多数局**。请调度器把 10 也算进 13 的前置。
- **A4 没有「脚本化牌山」的入口。** `GameState.start : Ruleset -> KyokuContext -> Rng -> ...`
  只能从 Rng 造牌山，而重放要求「配牌 = 牌谱给的 `tehais`、每次摸牌 = 牌谱给的 `pai`」。
  13 必须自己加一个 replay 入口（从 `start_kyoku` 事件构造 `KyokuStart` + 可注入 `tsumo` 的驱动）。
  这是本票最大的一块引擎侧工作，**别以为读进事件就完事了**。
- **A5 流局形态要靠推断或 oracle。** 牌谱里没有 `reason`：
  「前驱是 `tsumo` ⟹ 九種九牌，前驱是 `dahai` ⟹ 荒牌流局」这条判据在样本上 6/93 精确吻合，可用；
  但**听牌家在 deltas 全 0 时不可判**（全員聴牌 / 全員不聴 / 九種九牌 三者的 deltas 都是 0），
  必须查 `tests/fixtures/paifu/tenhou/` 里的 oracle。样本 99 次流局里有 11 次是这种。
- **A6 `RyuukyokuReason` 只有 `Fanpai`。** 样本需要至少再加九種九牌（12 票的范围）。
  在 12 票落地前，13 应当**显式跳过并计数**这些局（票里已经允许），不要静默当成荒牌流局。
- **A7 役种是日文字符串，不是我们的 `Yaku`。** oracle 给的是 `"立直(1飜)"` `"門前清自摸和(1飜)"`
  `"赤ドラ(2飜)"` 这种文本，样本里出现过 28 种（见下）。13 要写一张**役名对照表**
  （日文 → 我们的 `Yaku` case）并把「宝牌 / 赤宝牌 / 裏宝牌」三项单独拆出来对（它们在天凤是役行，
  在我们的 `Yaku` 里可能不是）。这张表是本票的隐藏工作量，**别低估**。

样本出现过的 28 种役（按频次）：赤ドラ 251、立直 235、ドラ 222、門前清自摸和 143、断幺九 106、
平和 103、裏ドラ 84、役牌 發 53、役牌 白 50、一発 40、役牌 中 38、混一色 24、場風 南 24、一盃口 23、
三色同順 19、場風 東 18、七対子 17、自風 南 16、自風 東 13、自風 北 12、一気通貫 9、対々和 7、
清一色 4、混全帯幺九 3、自風 西 3、純全帯幺九 2、嶺上開花 2、三暗刻 2。
**样本里没有役满、槍槓、海底/河底、三色同刻、小三元等**——这些的对拍要另找样本或人造用例。

### 第二档 —— 09 / 11 顺手就能补

- **B1 `dora` 事件 + 天凤的翻牌时机。** `ankan` 是**立即**翻（`ankan → dora → tsumo`），
  `kakan` / `daiminkan` 是**摸完岭上、打牌前**翻（`tsumo → dora → dahai`）。11 票加 `Event.Dora`
  时按这个时机产事件，13 就不用做时机对齐。
- **B2 立直是两个事件。** `reach`（宣言，打牌前）与 `reach_accepted`（成立，打牌后）。
  立直宣言牌被荣和时**只有 `reach`**（样本 12 次），立直棒不出。09 票照这个形状产事件。
  注意数据里 `reach_accepted` **不带 deltas**，−1000 是约定而非事实字段。
- **B3 副露事件的字段形状**（10 / 11 产事件时照抄，和 07 的 `Naki` 一一对应）：
  `pon`/`chi` = `actor` + `target` + `pai` + `consumed[2]`；`daiminkan` = 同上但 `consumed[3]`；
  `ankan` = `actor` + `consumed[4]`（**无 pai、无 target**）；`kakan` = `actor` + `pai` + `consumed[3]`。
- **B4 `start_game` 的 `kyoku_first` / `aka_flag`。** 我们只有 `names`；Thoth 会忽略多余字段所以
  不阻塞解码，但 round-trip 会掉字段。05 或 13 想做「读入→再编码」的等值测试时要先补上。

### 第三档 —— 不影响对拍

- **C1 `ura_markers` 的命名。** mjai 官方规格叫 `uradora_markers`，这个数据集写 `ura_markers`。
  我们的 `Hora` 目前根本没有里宝牌字段（09 票的事）。只比 deltas / 点数时用不到。
- **C2 `hora.pai` 缺失。** 和了牌可从紧邻的上一条 `tsumo` / `dahai` 推出，无损。
- **C3 `hora.scores` / `ryukyoku.scores` 缺失。** 用第三节的公式重建，60 局零误差。
- **C4 三麻扩展 `nukidora`：样本零出现**，该数据集不含三麻，不需要排除逻辑（加个断言即可）。
- **C5 `end_kyoku` / `end_game` 无字段**，与我们完全一致 ✓；`tsumo` / `dahai` / `start_kyoku`
  的字段名与顺序也与我们完全一致 ✓（只差记法映射）。
- **C6 老年份的脏数据。** 2009 样本里有一局跑了 34 个 kyoku、本场从 0 单调涨到 33、场风一路到
  `"N"`（掉线托管的马拉松局）。2026 样本无此现象。**建议只用近年数据**，或对局数设上限并跳过。

---

## 五、规则集提示（对应提案 S-A）

18 局固件与 60 局样本**全部**是同一套：天凤 JSON 的 `rule.disp = "鳳南喰赤"`，
`aka51 = aka52 = aka53 = 1`；牌谱 id 里的 `gm-00a9` 同义 = **鳳凰卓・南场（半庄）・喰断あり・赤あり**。

对照 S-A 表的天凤那一列，13 票对拍前应当这样配 `Ruleset`：

| 项 | 取值 | 依据 |
|---|---|---|
| `SeatCount` / `Length` | 4 / 半庄（东南） | 上游 README：只含四麻半庄；样本 `bakaze` ∈ {E,S,W} |
| `Akadora` | 5m / 5p / 5s **各一张**（换不是加） | `aka51=aka52=aka53=1`；样本 `5mr` `5pr` `5sr` 各出现 |
| `Kuitan` | **true** | `鳳南喰赤` 的「喰」 |
| `Atamahane` | **false（双响成立）** | 样本 3 次 `hora → hora`，实证 S-A 表「天凤ダブロンあり」 |
| `KiriageMangan` | **false** | **样本实证**：两个切上边界都按未切上给点——`30符4飜7700点`×30、`30符4飜2000-3900点`×16、`60符3飜7700点`×1（切上开着则应为 8000 / 2000-4000） |
| `DoubleKazeJantouFu` | **4** | S-A 表（天鳳雑スレ Wiki「連風牌は4符」）；样本未直接证伪，仍按 S-A |
| `StartingScore` / `RiichiBou` / `NotenBappu` / `HonbaPoints` | 25000 / 1000 / 3000 / 300 | 样本 `scores` 起始全 25000；`reach_accepted` 后 −1000；流局 deltas 为 ±1000/±1500/±3000 组合 |
| 西入 | 有（样本 4 局进 `bakaze = "W"`） | 天凤南场 30000 未达则西入 |

**没有实证到的**：连风牌雀头符、国士暗杠抢杠、三家和了、流し満貫——样本里一次都没出现，
S-A 表的这几行仍是文献证据，13 票的对拍**不会**验到它们。

---

## 六、固件子集（已提交）

`tests/fixtures/paifu/`，18 局 / 177 kyoku / **987,413 字节**（mjai 870,629 + 天凤 JSON 116,784），
用「按字节最小化的集合覆盖 + 随机重启」挑的，覆盖 60 局样本里出现过的**全部 75 项特征**
（事件类型 × 流局形态 × 役种 × 符档 × 点数档 × 双响/西入/本场/供托/里宝牌枚数）。
每局的挑选理由、来源、许可、以及三条「读之前必须知道」都写在
`tests/fixtures/paifu/README.md`。**没有动任何 `.fsproj`**——挂载方式留给 13 票的实现者。

全 18 局里独此一份的稀有项：大明槓、双响、西入、九種九牌（2 局）、全員聴牌 / 全員不聴、
50 符 / 60 符 / 70 符、倍満三档、跳満 18000、三暗刻、嶺上開花、混全帯幺九 / 純全帯幺九、自風 西。

---

## 七、留给人的待审项

1. **13 的 blocked-by 漏了 10 票**（碰/吃是样本里最高频的副露）。见 A3。
2. **天凤 JSON oracle 随仓库分发是否可接受**：上游 `LICENSE-DATA` 提醒二次分发要守天凤 ToS。
   我按「18 局小样本作测试固件」处理并在固件 README 里写清了来源与移除办法。若判定不宜，
   删 `tests/fixtures/paifu/tenhou/` 并改为现取即可（脚本在本报告第一节）。
3. **役名对照表（日文 → `Yaku`）算谁的活**：A7 是 13 票里没写、但绕不过去的一块。
4. **`Event` 与「牌谱读入类型」是否要分家**（A2 的建议）。这关系到 `Event` 的「必然完整」不变量。
5. **固件目录位置**：仓库已有的固件在 `tests/Janpo.Engine.Tests/fixtures/`，本批按派工要求落在
   `tests/fixtures/paifu/`。两处并存不影响任何构建（本批没有挂进 `.fsproj`），要统一的话整目录移动即可。
