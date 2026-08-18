//! 票 91 的探路件：把 Akagi v3 的内置 bot（`native_bot`）编成 wasm32-unknown-unknown，
//! 在浏览器里对一个固定局面出一手。
//!
//! **不用 wasm-bindgen**：ABI 是手写的四个 `extern "C"` 导出 + 线性内存里的 UTF-8 字节，
//! 于是产物是一个自足的 `.wasm`，`index.html` 用 `WebAssembly.instantiateStreaming` 直接起，
//! 不需要任何 JS 胶水包，也不需要往 `flake.nix` 里塞 `wasm-bindgen-cli`。
//!
//! 接缝就是 mjai：`probe_feed` 收一行 mjai JSON，`probe_decide` 回一段 JSON
//! （里面是 mjai 风格的动作 + top-3 候选）。
//!
//! 探路件不接牌桌（那是票 92），所以这里没有任何 janpo 侧的类型。

use std::cell::RefCell;

/// wasm32 上换掉默认分配器（std 的 dlmalloc）。
///
/// 原因是一条实打实的上游 bug：`dlmalloc-0.2.13/src/lib.rs:145` 的 `free` 丢掉了 `align`
/// 参数就去 `validate_size`，于是任何走 `memalign`（对齐 > 16）的分配在释放时
/// 必然撞 `assertion failed: psize <= size + max_overhead`。candle 的卷积走 `gemm`，
/// 而 `gemm` 要 SIMD 对齐的缓冲——**默认分配器下第一次前向传播就 trap**。
#[cfg(all(target_family = "wasm", not(target_feature = "atomics")))]
#[global_allocator]
static TALC: talc::wasm::WasmDynamicTalc = talc::wasm::new_wasm_dynamic_allocator();

use native_bot::defaults;
use native_bot::engine::{BotAction, Decision, Engine};

thread_local! {
    static ENGINE: RefCell<Option<Engine>> = const { RefCell::new(None) };
    /// panic 在 wasm32-unknown-unknown 上无处可打印（没有 WASI、没有 stderr），
    /// 于是把最后一条 panic 信息留在这里，JS 侧 trap 之后再来取。
    static LAST_PANIC: RefCell<String> = const { RefCell::new(String::new()) };
}

/// 装一个把 panic 信息存进 `LAST_PANIC` 的 hook（`panic = "abort"` 下 hook 仍会先跑）。
#[no_mangle]
pub extern "C" fn probe_install_panic_hook() {
    std::panic::set_hook(Box::new(|info| {
        let msg = info.to_string();
        LAST_PANIC.with(|cell| *cell.borrow_mut() = msg);
    }));
}

/// 取最后一条 panic 信息（指针 << 32 | 字节数），没有就是空串。
#[no_mangle]
pub extern "C" fn probe_last_panic() -> u64 {
    hand_out(LAST_PANIC.with(|cell| cell.borrow().clone()))
}

// ---------------------------------------------------------------------------
// 线性内存的进出口
// ---------------------------------------------------------------------------

/// JS 侧申请一段内存写入 UTF-8 字节；返回的指针必须由 `probe_free` 归还。
#[no_mangle]
pub extern "C" fn probe_alloc(len: usize) -> *mut u8 {
    // 一定要 `into_boxed_slice`：`Vec::with_capacity` 只保证「至少」这么大，
    // 而 `probe_free` 是按 `len == capacity` 还回去的。容量对不上就是堆损坏，
    // 症状是几十次调用之后随机 trap（这一坑当场踩到过）。
    Box::into_raw(vec![0u8; len].into_boxed_slice()).cast::<u8>()
}

/// 归还 `probe_alloc` 或 `probe_decide` 给出的内存。
///
/// # Safety
/// `ptr` / `len` 必须来自本模块的分配，且只归还一次。
#[no_mangle]
pub unsafe extern "C" fn probe_free(ptr: *mut u8, len: usize) {
    if !ptr.is_null() && len > 0 {
        drop(Vec::from_raw_parts(ptr, len, len));
    }
}

/// 把一段 `String` 交给 JS：高 32 位是指针，低 32 位是字节数。
fn hand_out(s: String) -> u64 {
    // 同样要 `into_boxed_slice`：`format!` 出来的 String 容量通常大于长度。
    let bytes = s.into_bytes().into_boxed_slice();
    let len = bytes.len();
    let ptr = Box::into_raw(bytes).cast::<u8>();
    ((ptr as u64) << 32) | (len as u64)
}

/// # Safety
/// `ptr` / `len` 必须指向 `probe_alloc` 给出的、装着合法 UTF-8 的那段内存。
unsafe fn take_str<'a>(ptr: *const u8, len: usize) -> Option<&'a str> {
    std::str::from_utf8(std::slice::from_raw_parts(ptr, len)).ok()
}

// ---------------------------------------------------------------------------
// 探针
// ---------------------------------------------------------------------------

/// 用内嵌的默认权重造一个引擎。`0` 成功，`-1` 失败。
#[no_mangle]
pub extern "C" fn probe_init(num_players: u32, seat: u32) -> i32 {
    match defaults::engine(num_players as u8, seat as u8) {
        Ok(engine) => {
            ENGINE.with(|cell| *cell.borrow_mut() = Some(engine));
            0
        }
        Err(_) => -1,
    }
}

/// 内嵌的权重有多少字节（4 人 / 3 人各一份，此处报当前局面用的那份）。
#[no_mangle]
pub extern "C" fn probe_weight_bytes(num_players: u32) -> u32 {
    let bytes = if num_players == 3 {
        defaults::WEIGHTS_3P
    } else {
        defaults::WEIGHTS_4P
    };
    bytes.len() as u32
}

/// 喂一行 mjai JSON。`1` = 被接受，`0` = 解析不了，`-1` = 还没 init。
///
/// # Safety
/// 见 [`take_str`]。
#[no_mangle]
pub unsafe extern "C" fn probe_feed(ptr: *const u8, len: usize) -> i32 {
    let Some(line) = take_str(ptr, len) else {
        return 0;
    };
    ENGINE.with(|cell| match cell.borrow_mut().as_mut() {
        Some(engine) => i32::from(engine.feed_line(line)),
        None => -1,
    })
}

/// 自检：不经过 JS 侧的任何内存进出，直接用内嵌的 fixture 跑一遍。
/// （用来分清「是推理路径坏了」还是「是我的 ABI 坏了」。）
#[no_mangle]
pub extern "C" fn probe_selftest() -> u64 {
    let fixture = include_str!("../../fixtures/tenpai-tsumogiri.jsonl");
    let mut engine = match defaults::engine(4, 0) {
        Ok(engine) => engine,
        Err(e) => return hand_out(error_json(&e.to_string())),
    };
    for line in fixture.lines().filter(|l| !l.trim().is_empty()) {
        if !engine.feed_line(line) {
            return hand_out(error_json(&format!("rejected: {line}")));
        }
    }
    let json = match engine.decide() {
        Ok(Some(decision)) => decision_json(&decision),
        Ok(None) => r#"{"ok":true,"decision":null}"#.to_string(),
        Err(e) => error_json(&e.to_string()),
    };
    hand_out(json)
}

/// 让它出一手，返回 JSON（形状见 `index.html`）。
#[no_mangle]
pub extern "C" fn probe_decide() -> u64 {
    let json = ENGINE.with(|cell| match cell.borrow_mut().as_mut() {
        Some(engine) => match engine.decide() {
            Ok(Some(decision)) => decision_json(&decision),
            Ok(None) => r#"{"ok":true,"decision":null}"#.to_string(),
            Err(e) => error_json(&e.to_string()),
        },
        None => error_json("engine not initialised"),
    });
    hand_out(json)
}

fn error_json(msg: &str) -> String {
    format!(
        r#"{{"ok":false,"error":{}}}"#,
        serde_json::to_string(msg).unwrap_or_else(|_| "\"?\"".to_string())
    )
}

pub fn decision_json(decision: &Decision) -> String {
    let candidates = decision
        .candidates
        .iter()
        .map(|(action, p)| format!(r#"{{"action":{},"p":{p}}}"#, action_json(action)))
        .collect::<Vec<_>>()
        .join(",");
    format!(
        r#"{{"ok":true,"decision":{{"action":{},"forced":{},"candidates":[{}]}}}}"#,
        action_json(&decision.action),
        decision.forced,
        candidates
    )
}

/// 把 `BotAction` 印成 mjai 风格的对象（票 92 要照这里核字段）。
pub fn action_json(action: &BotAction) -> String {
    let quoted = |s: &String| serde_json::to_string(s).unwrap_or_else(|_| "\"?\"".to_string());
    let list = |v: &Vec<String>| {
        format!(
            "[{}]",
            v.iter().map(quoted).collect::<Vec<_>>().join(",")
        )
    };
    match action {
        BotAction::Dahai { pai, tsumogiri } => format!(
            r#"{{"type":"dahai","pai":{},"tsumogiri":{tsumogiri}}}"#,
            quoted(pai)
        ),
        BotAction::Reach { pai } => {
            format!(r#"{{"type":"reach","reach_dahai":{}}}"#, quoted(pai))
        }
        BotAction::Pon {
            target,
            pai,
            consumed,
        } => format!(
            r#"{{"type":"pon","target":{target},"pai":{},"consumed":{}}}"#,
            quoted(pai),
            list(consumed)
        ),
        BotAction::Chi {
            target,
            pai,
            consumed,
        } => format!(
            r#"{{"type":"chi","target":{target},"pai":{},"consumed":{}}}"#,
            quoted(pai),
            list(consumed)
        ),
        BotAction::Daiminkan {
            target,
            pai,
            consumed,
        } => format!(
            r#"{{"type":"daiminkan","target":{target},"pai":{},"consumed":{}}}"#,
            quoted(pai),
            list(consumed)
        ),
        BotAction::Ankan { consumed } => {
            format!(r#"{{"type":"ankan","consumed":{}}}"#, list(consumed))
        }
        BotAction::Kakan { pai, consumed } => format!(
            r#"{{"type":"kakan","pai":{},"consumed":{}}}"#,
            quoted(pai),
            list(consumed)
        ),
        BotAction::Hora { target } => format!(r#"{{"type":"hora","target":{target}}}"#),
        BotAction::Kyushu => r#"{"type":"ryukyoku"}"#.to_string(),
        BotAction::Kita => r#"{"type":"nukidora"}"#.to_string(),
        BotAction::Pass => r#"{"type":"none"}"#.to_string(),
    }
}

// ---------------------------------------------------------------------------
// getrandom 的确定性桩
// ---------------------------------------------------------------------------
//
// `riichienv-core` 的牌山在 seed 为 None 时会去要系统随机源（`rand::rng()`），
// 而 wasm32-unknown-unknown 上 getrandom 没有默认后端。探路件**从不洗牌**
// ——牌山与手牌完全由喂进去的 mjai 事件决定——所以这里给一个固定序列的桩，
// 既躲开 wasm-bindgen 胶水，又让整条路径确定可复现。
// 票 92 若要在浏览器里真开局（自己生成牌山），必须换成 `crypto.getRandomValues`。

#[cfg(target_arch = "wasm32")]
mod getrandom_stub {
    use std::cell::Cell;

    thread_local! {
        static STATE: Cell<u64> = const { Cell::new(0x9E37_79B9_7F4A_7C15) };
    }

    fn fill(dest: *mut u8, len: usize) {
        STATE.with(|state| {
            let mut x = state.get();
            for i in 0..len {
                x ^= x << 13;
                x ^= x >> 7;
                x ^= x << 17;
                // SAFETY: 调用方保证 dest[..len] 可写。
                unsafe { dest.add(i).write(x as u8) };
            }
            state.set(x);
        });
    }

    /// getrandom 0.3 的 custom 后端钩子。
    ///
    /// # Safety
    /// 由 getrandom 调用，`dest[..len]` 可写。
    #[no_mangle]
    pub unsafe extern "Rust" fn __getrandom_v03_custom(
        dest: *mut u8,
        len: usize,
    ) -> Result<(), getrandom::Error> {
        fill(dest, len);
        Ok(())
    }
}
