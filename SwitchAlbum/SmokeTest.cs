using SwitchAlbum.Models;
using SwitchAlbum.Services;

namespace SwitchAlbum;

/// <summary>
/// 无界面冒烟测试（--smoke-test）：模拟设备全流程（扫描 → 存电脑 → 存手机 → 校验），
/// 结果写入 %TEMP%\SwitchAlbumSmokeTest.txt，退出码 0/1。
/// </summary>
public static class SmokeTest
{
    public static async void RunAsync()
    {
        var lines = new List<string>();
        var ok = true;
        var root = Path.Combine(Path.GetTempPath(), "sab-smoke-" + Guid.NewGuid().ToString("N"));

        try
        {
            DemoAlbumGenerator.Generate(root);
            var provider = new MockMediaProvider(root, Path.Combine(root, "phone"));
            var tracker = new DuplicateTracker(Path.Combine(root, "saved.json"));
            var settings = new SettingsService(Path.Combine(root, "settings.json"));
            settings.Current.SavePath = Path.Combine(root, "saved-pc");
            var saveService = new SaveService(tracker);
            var phoneSaveService = new PhoneSaveService(tracker, settings);

            var devices = await provider.GetDevicesAsync(CancellationToken.None);
            await using var switchSession = await provider.ConnectAsync(devices.Single(d => d.IsSwitch), CancellationToken.None);
            var scan = await AlbumScanner.ScanAsync(switchSession, null, CancellationToken.None);
            if (scan == null)
            {
                throw new Exception("scan null");
            }

            lines.Add($"games={scan.Games.Count} items={scan.AllItems.Count}");

            var pcResult = await saveService.SaveToPcAsync(
                scan.AllItems, switchSession, settings.Current.SavePath, autoRename: true, null, CancellationToken.None);
            lines.Add($"pc-save saved={pcResult.SavedCount} skipped={pcResult.SkippedCount} failed={pcResult.Failed.Count} cancelled={pcResult.Cancelled}");
            ok &= pcResult.SavedCount == scan.AllItems.Count && pcResult.Failed.Count == 0;

            var pcFiles = Directory.GetFiles(settings.Current.SavePath).Length;
            lines.Add($"pc-files={pcFiles}");
            ok &= pcFiles == scan.AllItems.Count;

            await using var phoneSession = await provider.ConnectAsync(devices.Single(d => !d.IsSwitch), CancellationToken.None);
            var phoneResult = await phoneSaveService.SaveToPhoneAsync(
                scan.AllItems, switchSession, phoneSession, autoRename: true, null, CancellationToken.None);
            lines.Add($"phone-save saved={phoneResult.SavedCount} skipped={phoneResult.SkippedCount} failed={phoneResult.Failed.Count}");
            ok &= phoneResult.SavedCount == scan.AllItems.Count;

            var phoneDcim = Path.Combine(root, "phone", "Internal shared storage", "DCIM", "SwitchAlbum");
            var phoneFiles = Directory.Exists(phoneDcim)
                ? Directory.GetFiles(phoneDcim, "*", SearchOption.AllDirectories).Length
                : 0;
            lines.Add($"phone-files={phoneFiles}");
            ok &= phoneFiles == scan.AllItems.Count;

            // 二次保存应全部跳过
            var again = await saveService.SaveToPcAsync(
                scan.AllItems, switchSession, settings.Current.SavePath, autoRename: true, null, CancellationToken.None);
            lines.Add($"pc-resave skipped={again.SkippedCount}");
            ok &= again.SkippedCount == scan.AllItems.Count && again.SavedCount == 0;

            // 封面链路：名称反查 → 图标直链 → 本地缓存
            var titleDb = TitleDbService.LoadEmbedded();
            var tid = titleDb?.GetTitleIdByName("塞尔达传说 王国之泪");
            var coverService = new CoverService(settings, Path.Combine(root, "covers"));
            try
            {
                var cover = await coverService.ResolveAsync(
                    tid, "塞尔达传说 王国之泪", titleDb?.GetIconUrl(tid!), CancellationToken.None);
                lines.Add($"cover tid={tid} icon={titleDb?.GetIconUrl(tid!)} path={cover}");
                ok &= tid == "0100F2C0115B6000" && cover != null && File.Exists(cover);
            }
            catch (Exception ex)
            {
                ok = false;
                lines.Add("cover exception: " + ex);
            }
        }
        catch (Exception ex)
        {
            ok = false;
            lines.Add("exception: " + ex);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                // 清理失败不影响结果
            }
        }

        lines.Add(ok ? "SMOKE PASS" : "SMOKE FAIL");
        var resultPath = Path.Combine(Path.GetTempPath(), "SwitchAlbumSmokeTest.txt");
        File.WriteAllLines(resultPath, lines);
        Environment.Exit(ok ? 0 : 1);
    }
}
