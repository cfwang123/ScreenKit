# 早上可用的 USB 投屏快照

- **Git 提交**：`ea00c15` — `fix: WiFi投屏认已选电脑，USB配件短超时读写避免黑屏`
- **相对提交的修正**：`CastService.broadcastStat` 改为 `sendBroadcast`（提交里递归调用会在 pattern 失败时 StackOverflow）

## 文件

| ref 路径 | 工程路径 |
|----------|----------|
| `android/CastSink.kt` | `android/app/src/main/java/com/whj/screenkit/CastSink.kt` |
| `android/CastService.kt` | `android/app/src/main/java/com/whj/screenkit/CastService.kt` |
| `ScreenKit/UsbHost.cs` | `ScreenKit/Cast/UsbHost.cs` |
| `ScreenKit/Proto.cs` | `ScreenKit/Cast/Proto.cs` |
| `ScreenKit/RecvSrv.cs` | `ScreenKit/Cast/RecvSrv.cs` |

## 自测

```powershell
powershell -File tmp/apply_ref_usb_test.ps1
```

（会先 `adb kill-server`，避免 adb reverse 占 WinUSB。）结果摘要：`last-test.log`。与工程目录对照：`powershell -File tmp/diff_ref_current.ps1`（应无差异）。
