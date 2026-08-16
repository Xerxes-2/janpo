// 浏览器入口。**这是 TS 侧唯一碰 F# 的地方**，而且只碰一个函数。
//
// 方向是 ADR-0005 定的：跨界只往「F# 调 TS」那一边走，TS 这边一行 import 就到头。
// `src/generated/` 是 Fable 的输出（gitignore 掉），由 `pnpm run fable` 生成。
import { mount } from "./generated/Main.js";
import "./styles.css";

mount("janpo-root");
