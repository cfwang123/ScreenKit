# 相对 git `ea00c15` 的修正（ref 与当前工程一致）

提交 `ea00c15` 自测 `scst_usb_pat` 会失败：`usb-r` 在 init 里立刻 `Os.read` 阻塞，导致 hello 经 aloop 写出要等约 8s（`hello ok=false`），PC 收不到首包。

| 项 | ea00c15 | ref / 当前 |
|----|---------|------------|
| `broadcastStat` | 递归调用自身（StackOverflow） | `sendBroadcast` |
| `usb-r` | init 即 `start()` | 握手：`sendJsonNow` → `startRead()` → `awaitHello` |
| hello 写出 | 仅 aloop 队列 | 握手阶段 `rawWrite` 同步写 |
| `helloLatch` | 单次 CountDownLatch | 每次握手 `prepareHelloWait()` 新建 |
| 读 PC 下行 | `Os.read` + `poll` | `FileInputStream` + `Proto.read`（写仍 `Os.write`） |
| PC `UsbHost.pump` | 写 hello 后启 copy | 先启 `usb→up` copy，hello 回包写 8 次 |

自测前建议 `adb kill-server`，避免 `USB adb 已转发` 占 WinUSB（见 `tmp/apply_ref_usb_test.ps1`）。
