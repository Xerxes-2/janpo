{
  description = "janpo — LLM 日麻对战平台的开发环境（dotnet SDK + uv）";

  # nixpkgs-weekly 是 Determinate Nix 默认的 nixpkgs 快照源，纯 tarball URL，
  # 任何现代 Nix 都能取；版本由 flake.lock 钉死，不依赖宿主机上装了什么。
  inputs.nixpkgs.url = "https://flakehub.com/f/DeterminateSystems/nixpkgs-weekly/*.tar.gz";

  outputs =
    { self, nixpkgs }:
    let
      systems = [
        "x86_64-linux"
        "aarch64-linux"
        "x86_64-darwin"
        "aarch64-darwin"
      ];
      forAllSystems = f: nixpkgs.lib.genAttrs systems (system: f nixpkgs.legacyPackages.${system});
    in
    {
      devShells = forAllSystems (pkgs: {
        default = pkgs.mkShell {
          name = "janpo";

          packages = [
            # 引擎与测试：dotnet SDK。global.json 用 rollForward=latestFeature 接住这里的补丁号。
            pkgs.dotnet-sdk_10
            # 测试期的外部 Python 工具入口（03 的向听 oracle、13 的牌谱转换）。
            # 只在测试/构建期用 `uv run --with <pkg>`，不得成为引擎或 CLI 的运行时依赖。
            pkgs.uv
          ];

          env = {
            DOTNET_ROOT = "${pkgs.dotnet-sdk_10}/share/dotnet";
            DOTNET_CLI_TELEMETRY_OPTOUT = "1";
            DOTNET_NOLOGO = "1";
            DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1";
          };

          # Fantomas 是 dotnet local tool（.config/dotnet-tools.json），
          # `dotnet tool restore` 后用 `dotnet fantomas` 调用，shell 内外同一版本。
          shellHook = ''
            echo "janpo dev shell — dotnet $(dotnet --version), uv $(uv --version)"
          '';
        };
      });

      formatter = forAllSystems (pkgs: pkgs.nixfmt-tree);
    };
}
