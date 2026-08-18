/**
 * 分享载荷的编解码（票 77）：**一份牌谱原文 ↔ 一串放得进 URL hash 的字符**。
 *
 * F# 侧的对应物是 `src/Janpo.Web/Share.fs`（ADR-0005：跨界只有「F# 调 TS」一个方向，
 * 且只传字符串）。**这一层不认识牌谱**——进来出去都只是文本，牌谱的形状全在引擎那边
 * （`Paifu.decoder`）；这里只管压缩与 URL 安全编码这两件事。
 *
 * **零依赖**：压缩用浏览器原生的 `CompressionStream`（异步，因此两个出口都是 Promise），
 * base64 用 `btoa` / `atob`。`ci-web.sh` 的依赖白名单守着这条。
 */

/**
 * 压缩格式。**是 `deflate`（zlib）而不是 `deflate-raw`**——票 77 实测之后改的。
 *
 * raw 那一档**没有校验和**：一份 7,463 字符的半庄载荷逐位置改坏一个字符，7,463 次里
 * **5,666 次照样解得开**，而解出来是**另一份**牌谱（同样的长度、同样 parse 得动的 JSON）。
 * 分享链接被聊天工具截断、被人手抄错一位时，对面读到的就会是一场悄悄不同的对局。
 * zlib 那一档带 Adler-32：同一趟 7,471 个位置里 7,468 个当场读不动，剩下 3 个解出来与原文
 * **逐字相同**（那几位落在末尾不承载信息的填充位上）。代价是 6 字节 / 8 个字符（+0.1%）。
 *
 * 两档都是 `CompressionStream` 自带的，不引任何依赖；`deflate` 还比 `deflate-raw` 早
 * 进浏览器（Chrome 80 vs 103）。
 */
const FORMAT = "deflate";

/**
 * 放进 URL hash 的字符集：**`+` `/` `=` 一个都不许出现**（出现了就得再转义一层，
 * 而那正是这道编码要省掉的事）。这条也是解那侧的第一道判读。
 */
const URL_SAFE = /^[A-Za-z0-9_-]+$/;

/** `String.fromCharCode(...bytes)` 一次摊不下几 KB 的实参（栈会爆），按块来。 */
const CHUNK = 0x8000;

/** 字节。**写死 `ArrayBuffer`**：`Uint8Array` 默认那个 `ArrayBufferLike` 包得下 `SharedArrayBuffer`，而 `Blob` 不收它。 */
type Bytes = Uint8Array<ArrayBuffer>;

/** 一段字节流过一个压缩 / 解压流。`CompressionStream` 是异步的，这就是过界那两个 Promise 的由来。 */
async function through(
  bytes: Bytes,
  stream: CompressionStream | DecompressionStream,
): Promise<Bytes> {
  const piped = new Blob([bytes]).stream().pipeThrough(stream);
  return new Uint8Array(await new Response(piped).arrayBuffer());
}

/** 字节 → base64url（无填充）。标准 base64 的三个字符就地换掉。 */
function toBase64Url(bytes: Bytes): string {
  let binary = "";
  for (let start = 0; start < bytes.length; start += CHUNK) {
    binary += String.fromCharCode(...bytes.subarray(start, start + CHUNK));
  }
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
}

/** base64url → 字节。字符集已经在 `decodePayload` 里判过，这里只管换回来。 */
function fromBase64Url(payload: string): Bytes {
  const binary = atob(payload.replaceAll("-", "+").replaceAll("_", "/"));
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index);
  }
  return bytes;
}

/** 出错时给人看的那一句里，把浏览器的原话截短——它是诊断，不是给机器解析的。 */
function detail(error: unknown): string {
  const message = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
  const flat = message.trim().replace(/\s+/g, " ");
  return flat.length > 80 ? `${flat.slice(0, 80)}…` : flat;
}

/**
 * 一份牌谱原文 → URL hash 里那一段。**压完再编，不做任何前缀与版本标记**
 * （ADR-0002：真要更短是渲染侧的优化，不是新格式）。
 */
export async function encodePayload(text: string): Promise<string> {
  const compressed = await through(new TextEncoder().encode(text), new CompressionStream(FORMAT));
  return toBase64Url(compressed);
}

/**
 * URL hash 里那一段 → 一份牌谱原文。**回的是一个信封 JSON**，与 `decide.ts` 同一种做法：
 * `{"text":"…"}` 或者 `{"error":"载荷读不动：…"}`。
 *
 * **它不抛，也不静静地回空手**（票 77）：读不动的四种失法各有各的中文原因，
 * 一律以「载荷读不动：」开头——F# 那侧靠这个前缀分得清是**载荷**读不动还是**牌谱**读不动
 * （后者是 `Paifu.decoder` 的诊断，措辞在 `Share.ofPayload`）。
 */
export async function decodePayload(payload: string): Promise<string> {
  const failure = (why: string) => JSON.stringify({ error: `载荷读不动：${why}` });

  if (payload === "") return failure("分享链接里没有载荷");
  if (!URL_SAFE.test(payload)) {
    const stray = [...payload].find((each) => !URL_SAFE.test(each));
    return failure(`载荷里混进了 base64url 之外的字符「${stray}」`);
  }

  let compressed: Bytes;
  try {
    compressed = fromBase64Url(payload);
  } catch (error) {
    return failure(`base64url 解不开（${detail(error)}）`);
  }

  let plain: Bytes;
  try {
    plain = await through(compressed, new DecompressionStream(FORMAT));
  } catch (error) {
    // 载荷被截断、被改过一个字符都落在这里：zlib 的 Adler-32 认得出来（见 `FORMAT`）。
    //
    // **浏览器那句原话要标明是它说的**：Chrome 在这里给的是 `TypeError: Failed to fetch`
    // （解压流是用 `Response` 抽干的）——原话听起来像网络出了事，而这一趟一个请求都没发。
    // 错的诊断比没有诊断更贵，因此前面那句话得把真正的毛病说在前头。
    return failure(
      `解压不开，多半是链接被截断或抄错了（载荷 ${payload.length} 字符，浏览器说 ${detail(error)}）`,
    );
  }

  try {
    // `fatal` 是故意的：默认那档会把坏字节替换成 U+FFFD，于是坏载荷会**静静地**变成一份
    // 读不动的牌谱，红在下一层——而它本来就该红在这一层。
    return JSON.stringify({ text: new TextDecoder("utf-8", { fatal: true }).decode(plain) });
  } catch (error) {
    return failure(`解压出来的不是 UTF-8 文本（${detail(error)}）`);
  }
}
