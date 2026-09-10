# SwitchAlbum — Switch 相册管家

通过 USB 连接 Nintendo Switch，把截图与视频方便地保存到电脑或安卓手机。

## 功能

- **游戏卡片墙**：类似 Switch 相册的界面，按游戏分组展示（封面 + 游戏名 + 照片/视频数量），支持「全部」视图
- **一键保存全部 / 保存所选**：默认保存到 `桌面\SwitchAlbum`，主页显示当前路径、可随时更改
- **自动重命名**：保存时按 `日期_游戏名_序号` 命名（可在设置中关闭，保留原文件名）
- **重复检测**：已保存过的项目自动标记「已保存」，再次保存时自动跳过
- **保存到安卓手机**：USB 直连手机，写入 `DCIM\SwitchAlbum\日期`（按拍摄日期分文件夹），手机图库立即可见
- **大图预览**：双击照片进入灯箱，方向键/滚轮切换，Esc 关闭；视频调用系统播放器
- **游戏封面**：内置约 3.5 万款游戏的中文/英文名库，封面自动从任天堂官方 CDN 获取并缓存；支持简体/繁体系统
- **过渡动画**：卡片错峰入场、视图切换、灯箱 crossfade、悬停动效；浅色/深色主题跟随系统

## 使用步骤

1. 用 USB 数据线连接 Switch 与电脑
2. 在 Switch 上进入 **系统设置 → 数据管理 → 管理截图与视频 → 通过 USB 连接复制到电脑**
3. 启动 SwitchAlbum，软件会自动扫描相册（也可点「重新扫描」）
4. 点「一键保存全部」或勾选后「保存所选」；手机连接电脑后点「保存到手机」

## 运行

| 产物 | 说明 |
|---|---|
| `SwitchAlbum-v1.0.0-single.zip` | 单个 exe（含运行时，约 144MB），双击即用；个别杀软可能误报，属单文件打包的已知现象 |
| `SwitchAlbum-v1.0.0-portable.zip` | 免安装目录版（约 7MB），需已安装 .NET 8 桌面运行时 |

设置保存在 `%LocalAppData%\SwitchAlbum\settings.json`，封面缓存 `covers\`，缩略图缓存 `thumbs\`，已保存记录 `saved.json`。

## 设置说明

- **保存位置**：默认 `桌面\SwitchAlbum`，可更改
- **自动重命名**：默认开启，`20250311_塞尔达传说 王国之泪_001.jpg`
- **手机目标设备**：默认自动检测；多部手机时可在设置中指定
- **SteamGridDB API 密钥**（可选）：封面备选图源（官网注册免费），用于官方图源缺失的游戏
- **模拟模式**：没有 Switch 时预览界面（生成演示数据）

## 封面数据

`Resources/titles.json`（约 5MB）由 [blawar/titledb](https://github.com/blawar/titledb) 的 CN.zh / HK.zh / US.en 生成，含名称与任天堂 eShop 图标直链；繁体名会额外注册简体变体（OpenCC 繁简表 + 品牌名别名，如 薩爾達→塞尔达、瑪利歐賽車→马力欧卡丁车）。重新生成：

```
dotnet run --project tools\TitleDbGenerator -- SwitchAlbum\Resources\titles.json <CN.zh.json> <HK.zh.json> <US.en.json>
```

## 开发

```
dotnet build SwitchAlbum.sln          # 构建
dotnet run --project SwitchAlbum.TestsHarness   # 纯函数与集成测试（30 项）
dotnet run --project SwitchAlbum -- --smoke-test # 无界面端到端冒烟（结果写入 %TEMP%\SwitchAlbumSmokeTest.txt）
```

打包：

```
dotnet publish SwitchAlbum -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\single
dotnet publish SwitchAlbum -c Release -r win-x64 --self-contained false -o publish\portable
```

## 已知限制

- 保存到手机仅支持安卓（MTP）；iPhone 不允许电脑通过 USB 写入相册
- 相册文件夹名依赖 Switch 系统语言；繁体/简体均已适配，未收录的游戏显示占位封面
- 固件 11.0.0 起 USB 复制模式按游戏名建文件夹；旧式 `<时间戳>-<titleid>` 文件夹也已兼容
