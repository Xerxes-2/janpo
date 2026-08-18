// 探路件自带的静态服务器（裁定 S-1：不许蹭主站的 `?dev=1` 那一侧）。
//   node serve.mjs [port]
// 默认 4191 —— 避开 verify-*.mjs 用的 4179–4182（判据 16：并行跑批时端口撞车会冒充真红）。
//
// .wasm 走 gzip，好让「首次加载」量到的是与 GitHub Pages 同形的传输量。
import { createReadStream, statSync } from "node:fs";
import { createServer } from "node:http";
import { createGzip } from "node:zlib";
import { dirname, extname, join, normalize } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const port = Number(process.argv[2] ?? 4191);

const MIME = {
  ".html": "text/html; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".mjs": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".jsonl": "text/plain; charset=utf-8",
  ".wasm": "application/wasm",
};

const server = createServer((req, res) => {
  const url = new URL(req.url, `http://localhost:${port}`);
  const rel = normalize(decodeURIComponent(url.pathname)).replace(/^(\.\.[/\\])+/, "");
  const path = join(here, rel === "/" ? "index.html" : rel);
  let size;
  try {
    size = statSync(path).size;
  } catch {
    res.writeHead(404).end("not found");
    return;
  }
  const type = MIME[extname(path)] ?? "application/octet-stream";
  const wantsGzip = (req.headers["accept-encoding"] ?? "").includes("gzip");
  const headers = { "content-type": type, "cache-control": "no-store" };
  if (wantsGzip) {
    headers["content-encoding"] = "gzip";
    res.writeHead(200, headers);
    createReadStream(path).pipe(createGzip({ level: 9 })).pipe(res);
  } else {
    headers["content-length"] = size;
    res.writeHead(200, headers);
    createReadStream(path).pipe(res);
  }
});

server.listen(port, () => console.log(`probe/akagi-wasm → http://localhost:${port}/`));
