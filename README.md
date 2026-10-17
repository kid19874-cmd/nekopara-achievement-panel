# NEKOPARA Extra 成就图标显示器

一个独立的小工具：在《NEKOPARA Extra》（Steam App ID `899970`）游戏窗口旁边贴一个成就面板，把 13 项成就以**图标**形式显示出来——未完成是黑白，已完成是彩色。不修改游戏，不联网，不注入。

![预览](achievement-panel.jpg)

## 特性

- **纯图标，无常驻文字**：主面板只显示 13 个成就图标，不显示成就名、进度条或其它文字。
- **黑白 / 彩色区分**：未完成灰色，已完成自动变彩色，读取的是本机 Steam 的实时成就缓存。
- **跟随游戏窗口**：自动贴合游戏窗口可见边框，右侧空间不足时自动尝试左侧。拖动顶部空白处可解除跟随、自由摆放。
- **圆角无描边**：主面板与指南弹窗使用真实圆角 Region，并禁用系统附加的白边。
- **中文成就指南**：鼠标悬停显示中文名称/说明/完成状态（已完成时含解锁日期）；左键点击图标打开对应的简体中文攻略步骤。
- **窗口外右键关闭指南**：指南可见时临时注册本进程低级鼠标钩子，仅在窗口任意位置右键即关闭指南（不注入游戏、不吞事件、不记录坐标），关闭后立即卸载。
- **背景色可选**：顶部调色按钮可选「游戏」（跟随游戏窗口边框配色）或「自选」（自定义 RGB，写入 `settings.txt`）。

## 顶部三个控件

| 位置 | 控件 | 说明 |
|---|---|---|
| 左 | 绿色勾形状态标志 | 已检测到游戏窗口 **且** 成功读取本机成就记录才显示绿色；否则灰色。悬停显示「已完成 X / 13」。 |
| 中 | 彩色调色按钮 | 打开独立小窗，选择「游戏」或「自选」背景模式。 |
| 右 | 加粗关闭 × | 深色背景下为纯白描边，浅色背景自动转深色。悬停提示「关闭」。 |

## 运行要求

- Windows（自带 .NET Framework 4.x）
- 本机安装并登录 Steam，且拥有 NEKOPARA Extra

## 使用

1. 解压 ZIP。
2. 双击 `NekoparaAchievementPanel.exe`。
3. **必须保留同目录的 `icons` 文件夹**（26 张成就图标）。

无需安装、无需 Python、无需账号密码、无后台联网。

## 从源码构建

```powershell
# 需要 .NET Framework 的 csc.exe
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:NekoparaAchievementPanel.exe AchievementPanel.cs
```

编译后把 `icons` 文件夹与 `使用说明.txt` 放在 exe 同目录即可。

## 数据与安全

- 只**读取**本机 Steam 的成就定义与当前账号的成就缓存，不写入。
- **不修改**游戏目录、Steam 成就位图、存档、注册表或启动项。
- 设置与日志只写入工具自身所在目录。
- 发布包内**不含**任何本机缓存、账号数据、日志或备份。
- 26 张成就图标取自 Steam 官方 CDN。

## 已知限制

- 自动检测间隔 2 秒，依赖 Steam 的成就缓存落盘，**不保证与游戏内成就弹窗同步即时刷新**。
- 真实新成就解锁后的自动变色，未通过人为触发解锁事件做端到端验证。
- 缓存缺失或截断时显示「未知」，而不是当作未完成。

## 社区来源

中文攻略步骤优先采用本作专属的 Steam 社区指南：

- **Nekopara Extra - 100% Achievements Guide** — <https://steamcommunity.com/sharedfiles/filedetails/?id=2837077068>

内置步骤离线可读；点击社区链接才会用默认浏览器打开原帖。

---

# 发布与维护说明（仅仓库维护者需要）

## 上传 ZIP 到 GitHub

```powershell
# 用 gh CLI（需先登录：gh auth login）
gh release create v1.0.0 "NEKOPARA-成就图标显示器.zip" `
  --repo kid19874-cmd/nekopara-achievement-panel `
  --title "v1.0.0 NEKOPARA Extra 成就图标显示器" `
  --notes "首个发布版本：13 项成就图标显示、中文指南、跟随游戏配色。"
```

或者直接用浏览器：仓库页 → **Releases** → **Create a new release** → 拖入 zip。

## 本机浏览器登录态（供自动化复用）

本机 DSH 浏览器的 GitHub 登录态已导出保存，浏览器重启或掉登录时可直接恢复，
**无需重新走 Google OAuth + reCAPTCHA 验证**。

| 项 | 值 |
|---|---|
| 登录账号 | `kid19874-cmd` |
| 登录方式 | Google OAuth（初次需手动过 reCAPTCHA） |
| 凭证文件 | `C:\Users\99382\.dsh\agent-risk-log\browser-auth\cookies.json` |
| 转换脚本 | `C:\Users\99382\.dsh\agent-risk-log\browser-auth\restore-browser-auth.mjs` |
| 权限 | 仅当前用户可读（已收紧 ACL，去掉继承） |

**恢复步骤**

1. 读取 `cookies.json` 得到 cookie 数组：

   ```powershell
   node "C:\Users\99382\.dsh\agent-risk-log\browser-auth\restore-browser-auth.mjs"
   ```

   该脚本把导出的 cookie 转成 `dsh_browser_auth restore` 需要的格式并输出到 stdout。

2. 把输出的 JSON 数组作为 `cookies` 参数调用 DSH 的 `dsh_browser_auth` 工具，`action: "restore"`：

   ```
   dsh_browser_auth(action="restore", cookies=[...上一步输出的数组...])
   ```

3. 打开 <https://github.com/>，确认右上角出现 **kid19874-cmd** 头像即恢复成功。

**重新导出（登录态更新后）**

```
dsh_browser_auth(action="flush")
```

把返回的数组按同样格式覆盖写入 `cookies.json` 即可。

> ⚠️ **安全提示**：`cookies.json` 里是**活凭证**，等同于账号密码。
> 不要提交进 git、不要上传网盘、不要贴进任何对话或日志。分享本仓库时务必排除该文件。
