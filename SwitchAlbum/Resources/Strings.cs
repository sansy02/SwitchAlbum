namespace SwitchAlbum.Resources;

/// <summary>全应用中文文案，XAML 中通过 {x:Static} 引用。</summary>
public static class Strings
{
    public const string App_Title = "Switch 相册管家";

    // 设备状态
    public const string Status_Scanning = "正在扫描设备…";
    public const string Status_Connected = "已连接";
    public const string Status_NotConnected = "未连接";
    public const string Status_Connecting = "正在连接…";
    public const string Hint_ConnectSwitch = "未检测到 Switch。请用 USB 连接电脑，并在主机上依次进入 系统设置 → 数据管理 → 管理截图与视频 → 通过 USB 连接复制到电脑";
    public const string Hint_EmptyAlbum = "相册为空，或未找到 Album 目录";
    public const string Status_RenamedNote = "已自动重命名 {0} 个文件";

    // 按钮
    public const string Btn_Rescan = "重新扫描";
    public const string Btn_SaveAll = "一键保存全部";
    public const string Btn_SaveSelected = "保存所选";
    public const string Btn_SaveToPhone = "保存到手机";
    public const string Btn_SaveSelectedToPhone = "保存所选到手机";
    public const string Btn_Settings = "设置";
    public const string Btn_ChangePath = "更改";
    public const string Btn_Cancel = "取消";
    public const string Btn_Back = "返回全部游戏";
    public const string Btn_SelectAll = "全选";
    public const string Btn_DeselectAll = "取消全选";
    public const string Btn_OpenLogs = "打开日志文件夹";
    public const string Btn_OpenVideo = "打开播放";

    // 标签
    public const string Lbl_SavePath = "保存位置";
    public const string Wall_SearchHint = "搜索游戏";
    public const string Wall_NoResults = "没有找到匹配的游戏";
    public const string Sort_NewestFirst = "从新到旧";
    public const string Sort_OldestFirst = "从旧到新";
    public const string Sort_Tooltip = "按拍摄时间排序";
    public const string Lbl_Selected = "已选 {0} 项";
    public const string Card_AllGames = "全部";
    public const string Card_PhotoCount = "{0} 张照片";
    public const string Card_VideoCount = "{0} 个视频";
    public const string Badge_Video = "视频";
    public const string Badge_Saved = "已保存";
    public const string Cover_Unknown = "无封面";

    // 保存进度 / 状态
    public const string Status_Progress = "正在保存 {0}/{1}：{2}";
    public const string Status_Skipped = "已跳过 {0} 个已保存项目";
    public const string Status_SavedDone = "已保存 {0} 个项目，跳过 {1} 个，失败 {2} 个";
    public const string Status_SavedPartial = "设备已断开，已保存 {0}/{1} 个项目";
    public const string Status_NoSelection = "请先勾选要保存的项目";
    public const string Status_NoPhone = "未检测到手机设备，请用 USB 连接手机并选择「传输文件」模式";
    public const string Status_PhoneDone = "已保存 {0} 个项目到手机，跳过 {1} 个";
    public const string Status_PhoneProgress = "正在上传 {0}/{1}：{2}";
    public const string Msg_PathInvalid = "保存路径不可用，请重新选择";
    public const string Msg_SaveFailed = "保存失败：{0}";

    // 设置
    public const string Dlg_Settings_Title = "设置";
    public const string Settings_Theme = "主题";
    public const string Settings_Theme_System = "跟随系统";
    public const string Settings_Theme_Light = "浅色";
    public const string Settings_Theme_Dark = "深色";
    public const string Settings_AutoRename = "保存时自动重命名（日期_游戏名_序号）";
    public const string Settings_PhoneDevice = "保存到手机的目标设备";
    public const string Settings_PhoneDevice_Auto = "自动检测";
    public const string Settings_ApiKey = "SteamGridDB API 密钥（可选，封面备选图源）";
    public const string Settings_PickFolder = "选择保存文件夹";
    public const string Settings_Close = "关闭";

    // 手机设备选择
    public const string Phone_Picker_Title = "选择手机设备";
    public const string Phone_Refresh = "刷新设备列表";
    public const string Phone_None = "未检测到手机设备";

    // 通用
    public const string Common_Ok = "确定";
    public const string Common_Cancel = "取消";
    public const string Lightbox_NoPreview = "视频无法预览，可保存后在手机上观看";
}
