/**
 * 版本号用的那个哈希（票 31 写在 `template.ts` 里，票 43 挪出来）。
 *
 * **单独一个文件是有理由的**：算渲染器摘要的那段走查（`scripts/renderer-digest.ts`）
 * 要用它，而它若与 `render-version.ts` 同处一室，走查就会 import 到自己的生成物
 * ——生成物一旦缺失，重新生成它的那个命令自己先跑不起来。这里没有任何 import。
 */

/**
 * FNV-1a（32 位）。**不是密码学哈希**：它只要「改一个字就换一个值」且跨运行稳定，
 * 用来当版本号的一截。按 UTF-16 码元走，因此同一份文本在哪台机器上都是同一个值。
 */
export function fnv1a(text: string): string {
  let hash = 0x811c9dc5;
  for (let index = 0; index < text.length; index += 1) {
    hash ^= text.charCodeAt(index);
    // FNV 质数 16777619，用移位加法算，避免 32 位乘法溢出成浮点。
    hash = (hash + ((hash << 1) + (hash << 4) + (hash << 7) + (hash << 8) + (hash << 24))) >>> 0;
  }
  return hash.toString(16).padStart(8, "0");
}
