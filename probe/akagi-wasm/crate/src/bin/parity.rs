//! 原生（x86_64）侧跑同一份 fixture，用来与浏览器里的 wasm 逐字对拍。
//!
//! 这不是「另写一份实现」——两侧是同一份 `native_bot` 源码，只是编译目标不同。
//! 它要证的正是那件唯一可疑的事：**wasm 后端的浮点算出来的策略与原生一致**。
//!
//! ```sh
//! cargo run --release --bin parity -- ../fixtures/tenpai-tsumogiri.jsonl
//! ```

use std::env;
use std::fs;
use std::time::Instant;

use akagi_wasm_probe::decision_json;
use native_bot::defaults;

fn main() -> anyhow::Result<()> {
    let path = env::args()
        .nth(1)
        .unwrap_or_else(|| "../fixtures/tenpai-tsumogiri.jsonl".to_string());
    let reps: usize = env::args()
        .nth(2)
        .and_then(|s| s.parse().ok())
        .unwrap_or(200);
    let text = fs::read_to_string(&path)?;

    let t0 = Instant::now();
    let mut engine = defaults::engine(4, 0)?;
    let init_ms = t0.elapsed().as_secs_f64() * 1e3;

    for line in text.lines().filter(|l| !l.trim().is_empty()) {
        if !engine.feed_line(line) {
            anyhow::bail!("engine rejected mjai line: {line}");
        }
    }

    let decision = engine.decide()?.ok_or_else(|| anyhow::anyhow!("no decision"))?;
    let json = decision_json(&decision);

    // 同一个决策点重复 decide()：decide 对局面是只读的，所以这就是纯推理延迟。
    let mut samples = Vec::with_capacity(reps);
    for _ in 0..reps {
        let t = Instant::now();
        let d = engine.decide()?.expect("decision");
        samples.push(t.elapsed().as_secs_f64() * 1e3);
        assert_eq!(decision_json(&d), json, "同一局面上 decide() 不确定");
    }
    samples.sort_by(|a, b| a.partial_cmp(b).unwrap());
    let median = samples[samples.len() / 2];
    let p95 = samples[(samples.len() as f64 * 0.95) as usize % samples.len()];

    println!("{json}");
    println!(
        "{{\"init_ms\":{init_ms:.3},\"reps\":{reps},\"median_ms\":{median:.3},\"p95_ms\":{p95:.3}}}"
    );
    Ok(())
}
