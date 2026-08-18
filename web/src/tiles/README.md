# 牌面 SVG（票 80）

来源：[FluffyStuff/riichi-mahjong-tiles](https://github.com/FluffyStuff/riichi-mahjong-tiles)
的 `Regular/` 那一套，commit `26e127ba2117f45cdce5ea0225748cc0cfad3169`（2024-06-15）。
**CC0 公共领域**（上游 `LICENSE.md`：“This work is in the public domain”），无需署名；
仍记来源是为了下次有人想换套牌面时知道从哪儿拿、拿的是哪一版。

共 38 张：34 种正牌（`Man1`–`Man9`、`Pin1`–`Pin9`、`Sou1`–`Sou9`、
`Ton`/`Nan`/`Shaa`/`Pei`/`Haku`/`Hatsu`/`Chun`）+ 三张赤五（`Man5-Dora`/`Pin5-Dora`/`Sou5-Dora`）
+ 牌背（`Back`）。上游的 `Front.svg` / `Blank.svg` 没拿——牌框（象牙白圆角）由
`styles.css` 自己画，SVG 只当牌面花色。

怎么用：`styles.css` 按 `data-pai`（mjai 记法，`Tile.toMjai` 的输出）逐张
`background-image: url("./tiles/….svg")`，牌背走 `.tile.back`。Vite 打包时把它们
当普通资产哈希进 `dist/assets/`（都超过 4 KB 内联阈值，不会变成 data URI）。
