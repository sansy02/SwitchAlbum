# SwitchAlbum — Switch 相册管家

通过 USB 连接 Nintendo Switch，把截图与视频方便地保存到电脑或安卓手机。

## 功能

- **游戏卡片墙**：类似 Switch 相册的界面，按游戏分组展示（封面 + 游戏名 + 照片/视频数量），支持「全部」视图与**搜索游戏**
- **一键保存全部 / 保存所选**：默认保存到 `桌面\SwitchAlbum`，主页显示当前路径、可随时更改
- **自动重命名**：保存时按 `日期_游戏名_序号` 命名（可在设置中关闭，保留原文件名）
- **重复检测**：已保存过的项目自动标记「已保存」，再次保存时自动跳过
- **保存到安卓手机**：USB 直连手机，写入 `DCIM\SwitchAlbum\日期`（按拍摄日期分文件夹），手机图库立即可见
- **大图预览**：双击照片进入灯箱，方向键/滚轮切换，Esc 关闭；视频调用系统播放器
- **按拍摄时间排序**：从新到旧 / 从旧到新（默认从新到旧，与 Switch 相册一致）
- **游戏封面**：内置约 3.5 万款游戏的中文/英文名库；**3000 款热门游戏封面离线内置**（zip 打包、相册出现该游戏才解压、无需网络），其余游戏联网从任天堂官方 CDN 获取并缓存（图标/横幅多图源，覆盖率 99.6%）；支持简体/繁体系统
- **过渡动画**：卡片错峰入场、视图切换、灯箱 crossfade、悬停动效；浅色/深色主题跟随系统

## 下载与使用

从 [Releases](https://github.com/sansy02/SwitchAlbum/releases) 下载最新版 `SwitchAlbum-vX.X.X-single.zip`（单文件 exe，含运行时），解压双击即可。

1. 用 USB 数据线连接 Switch 与电脑
2. 在 Switch 上进入 **系统设置 → 数据管理 → 管理截图与视频 → 通过 USB 连接复制到电脑**
3. 启动 SwitchAlbum，软件会自动扫描相册（也可点「重新扫描」）
4. 点「一键保存全部」或勾选后「保存所选」；手机连接电脑后点「保存到手机」

## 设置说明

- **保存位置**：默认 `桌面\SwitchAlbum`，可更改
- **自动重命名**：默认开启，`20250311_塞尔达传说 王国之泪_001.jpg`
- **手机目标设备**：默认自动检测；多部手机时可在设置中指定
- **SteamGridDB API 密钥**（可选）：封面备选图源（官网注册免费），用于官方图源缺失的游戏
- **打开日志文件夹**：连接问题排查

数据目录 `%LocalAppData%\SwitchAlbum\`：`settings.json`（设置）、`covers\`（封面缓存）、`thumbs\`（缩略图缓存）、`saved.json`（已保存记录）、`logs\`（运行日志）。

## 封面数据

`Resources/titles.json`（约 8MB）由 [blawar/titledb](https://github.com/blawar/titledb) 的 CN.zh / HK.zh / US.en 生成，含名称与任天堂 eShop 图标/横幅直链；繁体名会额外注册简体变体（OpenCC 繁简表 + 品牌名别名，如 薩爾達→塞尔达、瑪利歐賽車→马力欧卡丁车）。重新生成：

```
dotnet run --project tools\TitleDbGenerator -- SwitchAlbum\Resources\titles.json <CN.zh.json> <HK.zh.json> <US.en.json>
```

## 开发

```
dotnet build SwitchAlbum.sln          # 构建
dotnet run --project SwitchAlbum.TestsHarness   # 纯函数与集成测试（30 项）
dotnet run --project SwitchAlbum -- --smoke-test # 无界面端到端冒烟（结果写入 %TEMP%\SwitchAlbumSmokeTest.txt）
```

打包（单文件 exe）：

```
dotnet publish SwitchAlbum -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\single
```

图标由 `tools\IconGenerator` 生成（橙色像素风 N + 白色描边）。

## 已知限制

- 保存到手机仅支持安卓（MTP）；iPhone 不允许电脑通过 USB 写入相册
- 相册文件夹名依赖 Switch 系统语言；繁体/简体均已适配，未收录的游戏显示占位封面
- 单文件 exe 可能被个别杀软误报，属无签名单文件打包的已知现象（右键属性 → 解除锁定后可正常运行）
