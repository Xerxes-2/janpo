#!/usr/bin/env python3
"""27 号验收：把一份导出的牌谱摊成报告要的那几个数。

只读 JSON，不重实现任何引擎逻辑（AGENTS.md：Python 只用于解析数据）。
用法：python3 /tmp/janpo-27-analyze.py /tmp/janpo-27-bare.json [截断手数]
"""

import json
import sys
from statistics import median

path = sys.argv[1]
cut = int(sys.argv[2]) if len(sys.argv) > 2 else None
paifu = json.load(open(path))

decisions = paifu["decisions"]
if cut:
    decisions = decisions[:cut]

events = paifu["events"]
kinds = {}
for e in events:
    kinds[e["type"]] = kinds.get(e["type"], 0) + 1

print(f"文件 {path}  {len(open(path,'rb').read())} 字节")
print(f"版本 {paifu['version']}  事件 {len(events)} 条  决策记录 {len(paifu['decisions'])} 条"
      + (f"（本次统计前 {cut} 条）" if cut else ""))
print("事件类型：" + "　".join(f"{k}×{v}" for k, v in sorted(kinds.items(), key=lambda kv: -kv[1])))
print()

lat = [d["latency_ms"] for d in decisions]
attempts = [d["attempts"] for d in decisions]
fallbacks = [d for d in decisions if d.get("fallback")]
thinking = [d for d in decisions if d.get("thinking")]
tails = [len(d.get("prompt_tail", d.get("prompt", ""))) for d in decisions]

def pct(values, p):
    s = sorted(values)
    return s[min(len(s) - 1, int(round((len(s) - 1) * p)))]

print(f"延迟 ms：最小 {min(lat)}　p25 {pct(lat,.25)}　中位 {int(median(lat))}　p75 {pct(lat,.75)}"
      f"　p90 {pct(lat,.90)}　最大 {max(lat)}　均值 {sum(lat)//len(lat)}")
print(f"重试次数分布：" + "　".join(f"{n} 次×{attempts.count(n)}" for n in sorted(set(attempts))))
print(f"兜底 {len(fallbacks)} 手　带 thinking {len(thinking)} 条")
for d in fallbacks:
    print(f"  turn {d['turn']}：{d['fallback']}")
print(f"prompt 尾部：合计 {sum(tails)} 字　首手 {tails[0]}　末手 {tails[-1]}　最大 {max(tails)}")
print()

use = [d.get("usage") or {} for d in decisions]
inp = sum(u.get("input", 0) for u in use)
out = sum(u.get("output", 0) for u in use)
cr = sum(u.get("cache_read", 0) for u in use)
cw = sum(u.get("cache_write", 0) for u in use)
prompt_total = inp + cr
print(f"token：prompt 合计 {prompt_total}（付全价 {inp}，命中缓存 {cr} = {cr*100//max(prompt_total,1)}%）"
      f"　写缓存 {cw}　输出 {out}")

# DeepSeek 官方价表（deepseek-chat，2026-08 现价，报告里写明它会漂）
IN_HIT, IN_MISS, OUT = 0.07, 0.56, 1.68  # USD / 1M tok
cost = cr / 1e6 * IN_HIT + inp / 1e6 * IN_MISS + out / 1e6 * OUT
print(f"按 $0.07/$0.56/$1.68 每 1M（命中/未命中/输出）估算：${cost:.4f}")
print(f"命中全废时（全部按未命中算）：${(prompt_total/1e6*IN_MISS + out/1e6*OUT):.4f}")
print()

print("逐手（每 5 手一行）：手 / prompt tok / 付全价 / 命中 / 命中率 / 延迟 ms / 尾部字数")
for i, (d, u) in enumerate(zip(decisions, use), start=1):
    if i % 5 and i != len(decisions):
        continue
    p = u.get("input", 0) + u.get("cache_read", 0)
    print(f"  {i:3d}  {p:6d}  {u.get('input',0):6d}  {u.get('cache_read',0):6d}"
          f"  {u.get('cache_read',0)*100//max(p,1):3d}%  {d['latency_ms']:6d}  {len(d.get('prompt_tail','')):5d}")

print()
print(f"prompting.preambles：{len(paifu.get('prompting', {}).get('preambles', []))} 份")
for pre in paifu.get("prompting", {}).get("preambles", []):
    print(f"  座位 {pre['seat']}　渲染版本 {pre['render_version']}　{len(pre['text'])} 字")
print(f"prompting.tools：{len(paifu.get('prompting', {}).get('tools',''))} 字")
