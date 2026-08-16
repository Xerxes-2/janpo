#!/usr/bin/env bash
# 跑批看门狗：记录负载历史，并把辅助工具进程自动降优先级。
#
# 存在的理由（2026-08-16）：一个研究 agent 开了 16 个 Python worker 扫语料，
# load average 冲到 32，把并行跑 dotnet 的另一个 agent 挤慢了 6 倍（CI 46s → 5m2s）。
# 唯一发现它的途径是用户听见风扇呼啸——这套跑批当时对资源零可见性。
#
# 策略：dotnet（真正的构建与测试）保持正常优先级；python3/uv/node 这类辅助工具
# 只要累计 CPU 时间超过阈值就自动 nice 19。不杀进程——只让它们吃空闲核心。
set -uo pipefail

LOG="${1:-.scratch/llm-riichi-arena/run/RESOURCES.log}"
INTERVAL="${WATCHDOG_INTERVAL:-30}"
CPU_SECONDS_BEFORE_NICE="${WATCHDOG_CPU_BUDGET:-90}"

mkdir -p "$(dirname "$LOG")"
printf '# 每 %ss 采样一次。辅助工具进程累计 CPU 超 %ss 自动 nice 19。\n' "$INTERVAL" "$CPU_SECONDS_BEFORE_NICE" >> "$LOG"

while true; do
    load=$(cut -d' ' -f1-3 /proc/loadavg)
    top=$(ps -eo pcpu,comm --sort=-pcpu --no-headers | head -3 | awk '{printf "%s:%s%% ", $2, $1}')
    printf '%s load=%s  %s\n' "$(date +%H:%M:%S)" "$load" "$top" >> "$LOG"

    # 只降辅助工具，不动 dotnet
    while read -r pid cputime nice comm; do
        [ -z "${pid:-}" ] && continue
        [ "$nice" -ge 19 ] 2>/dev/null && continue
        secs=$(awk -F: '{ if (NF==3) print $1*3600+$2*60+$3; else if (NF==2) print $1*60+$2; else print $1 }' <<< "$cputime")
        secs=${secs%.*}
        if [ "${secs:-0}" -ge "$CPU_SECONDS_BEFORE_NICE" ]; then
            renice -n 19 -p "$pid" >/dev/null 2>&1 &&
                printf '%s  ↓ nice 19: pid %s %s（已耗 CPU %ss）\n' "$(date +%H:%M:%S)" "$pid" "$comm" "$secs" >> "$LOG"
        fi
    done < <(ps -eo pid,cputime,nice,comm --no-headers | grep -E '(python3|uv|node|ruby|perl)$')

    sleep "$INTERVAL"
done
