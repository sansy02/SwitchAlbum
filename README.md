# SwitchPoter — Switch 相册管家

通过 USB 连接 Nintendo Switch，把截图与视频方便地保存到电脑或安卓手机。

## 功能

- **游戏卡片墙**：类似 Switch 相册的界面，按游戏分组展示（封面 + 游戏名 + 照片/视频数量），支持「全部」视图与**搜索游戏**
- **顶栏三个紫色切换标签**：「保存到此电脑」「保存到手机（安卓）」「保存到手机（苹果）」，当前页紫底白字、其余白底紫字，一键切换
- **保存到此电脑**：勾选照片后点「保存所选」，按**游戏分文件夹**保存到 `桌面\SwitchAlbum\游戏名\`，主页显示当前路径、可随时更改；进度条实时显示**传输速度**
- **保存到手机（安卓）**：浏览电脑上保存过的全部照片，勾选后传到安卓手机（MTP），手机里同样按游戏分文件夹（`DCIM\SwitchAlbum\游戏名`），图库立即可见；自动识别 MTP 手机，移动硬盘/U 盘不会被误认
- **保存到手机（苹果）**：**零安装局域网方案**——点「iPhone 连接入口」显示二维码，iPhone 同一 WiFi 扫码后在浏览器浏览全部照片：长按单张存入相册，或「下载 ZIP」后在文件 App 解压多选批量存入相册（iOS 平台限制下的官方允许流程）
- **自动重命名**：保存时按 `日期_游戏名_序号` 命名（可在设置中关闭，保留原文件名）
- **重复检测**：已保存过的项目自动标记「已保存」，再次保存时自动跳过
- **大图预览**：双击照片进入灯箱，方向键/滚轮切换，Esc 关闭；视频调用系统播放器
- **按拍摄时间排序**：从新到旧 / 从旧到新（默认从新到旧，与 Switch 相册一致）
- **游戏封面**：内置约 3.5 万款游戏的中/英/日文名库；**3000 款热门游戏封面离线内置**（zip 打包、相册出现该游戏才解压、无需网络），其余游戏联网从任天堂官方 CDN 获取并缓存（图标/横幅多图源）；支持简体/繁体系统
- **Switch 2 兼容**：文件夹名智能反查（自动剥离「Nintendo Switch 2 Edition」、DEMO、括号注释等后缀，支持日文/繁体/别名与包含匹配），封面命中率大幅提升；窗口位置自愈（显示器布局变化后不会跑到屏幕外）
- **过渡动画**：卡片错峰入场、视图切换、灯箱 crossfade、悬停动效；浅色/深色主题跟随系统

## 下载与使用

从 [Releases](https://github.com/sansy02/SwitchAlbum/releases) 下载最新版 `SwitchAlbum-vX.X.X-single.zip`（单文件 exe，含运行时），解压双击即可。

1. 用 USB 数据线连接 Switch 与电脑
2. 在 Switch 上进入 **系统设置 → 数据管理 → 管理截图与视频 → 通过 USB 连接复制到电脑**
3. 启动 SwitchAlbum，软件会自动扫描相册（也可点「重新扫描」）
4. 勾选照片点「保存所选」存到电脑；点「保存到手机（安卓）」传安卓手机；点「保存到手机（苹果）」扫码用局域网传到 iPhone

## 设置说明

- **保存位置**：默认 `桌面\SwitchAlbum`，可更改
- **自动重命名**：默认开启，`20250311_塞尔达传说 王国之泪_001.jpg`
- **手机目标设备**：默认自动检测；多部手机时可在设置中指定
- **打开保存文件夹**：资源管理器中打开保存目录
- **清除封面与缩略图缓存**：封面获取失败后的补救手段，清除后重新联网获取
- **打开日志文件夹**：连接问题排查

数据目录 `%LocalAppData%\SwitchAlbum\`：`settings.json`（设置）、`covers\`（封面缓存）、`thumbs\`（缩略图缓存）、`saved.json`（已保存记录）、`logs\`（运行日志）。

## 封面数据

`Resources/titles.json`（约 10MB）由 [blawar/titledb](https://github.com/blawar/titledb) 的 CN.zh / HK.zh / US.en 生成，并合并 JP.ja 日文名，含名称与任天堂 eShop 图标/横幅直链；繁体名会额外注册简体变体（OpenCC 繁简表 + 品牌名别名，如 薩爾達→塞尔达、瑪利歐賽車→马力欧卡丁车）。重新生成：

```
dotnet run --project tools\TitleDbGenerator -- SwitchAlbum\Resources\titles.json <CN.zh.json> <HK.zh.json> <US.en.json> [JP.ja.json]
dotnet run --project tools\JaMerger -- SwitchAlbum\Resources\titles.json <JP.ja.json> <输出titles.json>   # 仅合并日文名（titledb 现版 CN.zh 已空，勿从零重新生成）
```

注意：titledb 现版 CN.zh 仅剩腾讯存根，直接用 TitleDbGenerator 重新生成会丢失中文名；日常更新请用 JaMerger 合并日文名。

## 开发

```
dotnet build SwitchAlbum.sln          # 构建
dotnet run --project SwitchAlbum.TestsHarness   # 纯函数与集成测试（48 项）
dotnet run --project SwitchAlbum -- --smoke-test # 无界面端到端冒烟（结果写入 %TEMP%\SwitchAlbumSmokeTest.txt）
```

打包（单文件 exe）：

```
dotnet publish SwitchAlbum -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\single
```

图标由 `tools\IconGenerator` 生成（橙色像素风 N + 白色描边）。

## 已知限制

- 保存到安卓手机仅支持 MTP 模式（手机需选「传输文件」）；iPhone 通过 USB 无法写入相册（iOS 平台限制），本软件的苹果方案为零安装局域网网页（扫码 → 浏览器 → ZIP 批量存入相册）
- 同一张照片若曾用旧版「Switch 直连手机」保存过，再经「电脑→手机」会得到两份副本（去重键不同，属已知边界）
- 相册文件夹名依赖 Switch 系统语言；中文/英文/日文文件夹名均已适配，未收录的游戏显示占位封面
- 单文件 exe 可能被个别杀软误报，属无签名单文件打包的已知现象（右键属性 → 解除锁定后可正常运行）
