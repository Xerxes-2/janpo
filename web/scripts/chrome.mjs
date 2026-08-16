// 无头浏览器的查找。两个闸门脚本（verify-tracer / verify-golden）共用同一条查找顺序，
// 免得「机器上有浏览器但只有一个脚本找得到」。
//
// 顺序：$JANPO_CHROME → playwright 自带的 chromium → 系统里的 chrome/chromium。

import { existsSync } from "node:fs";
import { chromium } from "playwright-core";

export function chromeExecutable() {
  if (process.env.JANPO_CHROME) return process.env.JANPO_CHROME;
  try {
    const bundled = chromium.executablePath();
    if (bundled && existsSync(bundled)) return bundled;
  } catch {
    // playwright-core 没装过浏览器时会抛，落到下面的系统路径。
  }
  const candidates = [
    "/usr/bin/google-chrome-stable",
    "/usr/bin/google-chrome",
    "/usr/bin/chromium",
    "/usr/bin/chromium-browser",
  ];
  return candidates.find((path) => existsSync(path)) ?? null;
}

/** 找不到浏览器时的统一说法。装法与逃生口都写在这里。 */
export const missingChrome =
  "找不到可用的 Chrome/Chromium。装一个（`pnpm dlx playwright install chromium`）" +
  "或用 JANPO_CHROME=<路径> 指过去。";
