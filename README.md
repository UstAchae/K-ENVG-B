# K-ENVG-B

ENVG-B（AN/PSQ-42 增强型夜视仪）头盔夜视仪，用于 SPT 4.1.6：双目白磷夜视，右目镜带热像，可以显示热像轮廓或热像融合（白热 / 黑热），用方向键切换。

Justachae's ENVG-B (AN/PSQ-42) for SPT 4.1.6: a helmet night vision item with thermal outline and thermal fusion (white hot / black hot), switched with the arrow keys.

## 0.73.3 beta 1：测试版

这是测试版本，还在游戏里测试中，可能会有改动或问题，发现不对的地方欢迎反馈。

This is a test version: the mod is still being tested in game, and things may change or break. Please report what looks wrong.

## 物品

都在维和部队（Peacekeeper）出售。

| 物品 | 用途 | 价格 / 信任等级 |
|---|---|---|
| **ENVG-B**（黑色 / 沙色） | 双目白磷夜视仪，右目镜带热像模块，有目镜显示信息 | $1890，LL4 |
| **ENVG-B 眼罩**（黑色 / 沙色） | 装在 ENVG-B 的眼罩槽上：贴住脸，挡住目镜四周漏进来的光；戴不戴眼罩，眼睛离镜片一样近（K-3PS-C 1.9.0） | $30，LL1，每次刷新限购 4 个 |

ENVG-B 能装在所有能装 GPNVG-18 的地方（头盔夜视仪槽、各种夜视仪支架），其他模组加的头盔和支架只要能装 GPNVG-18，也能装它。冲突规则和 GPNVG-18 相同；另外它和 K-FPANO 的 RAILINK 导轨冲突。

## 快捷键

| 按键 | 作用 |
|---|---|
| 游戏的夜视仪开关（默认 **N**） | 翻下 / 翻上 ENVG-B |
| **→** / **←** | ENVG-B 翻下时：切换模式（纯夜视 → 轮廓 → 白热融合 → 黑热融合） |
| **↑** / **↓** | 调夜视管增益：点一下微调，按住连续变化（从 0 到满约 2.5 秒）；调节时热像框左边显示黄色的 I2 GAIN 竖条；调到 0 夜视画面全黑，只剩热像（轮廓模式下只剩轮廓） |
| **Alt + ←** | 把左目镜横着掰起或放回（真机两个目镜可以各自掰起）。掰起后左边的夜视管关闭 |
| **Alt + →** | 把右目镜掰起或放回。右目镜带着热像模块，掰起后热像融合、轮廓和目镜信息都关闭 |

快捷键都可以在 F12 设置的「ENVG-B 热像轮廓」一栏里改。菜单、背包或地图打开时方向键不起作用。

配合 K-3PS-C 时，支架位置可以调：腰射时按住 **左 Alt + = / -** 升降，**左 Shift + = / -** 仰俯，**左 Ctrl + = / -** 前后；每个支架各自记住位置。

## 四种模式

- **纯夜视**：只有白磷夜视。
- **轮廓**：温差的边缘画成琥珀色的线。
- **白热融合**：整幅热像叠在夜视上，热的亮。
- **黑热融合**：整幅热像叠在夜视上，冷的亮、热的暗。

### 目镜显示信息

热像框里显示青白色、带荧光的信息：
- 左上：所在位置的网格坐标，以及目镜正前方的距离；
- 上方正中：罗盘刻度带和朝向；
- 右上：时间（祖鲁时间）和画面来源；
- 左下：电池电量；
- 下方正中：当前显示模式；
- 调增益时左边出现 I2 GAIN 竖条。

F12 里「目镜显示信息」可以关掉这些。

### F12 里常用的设置（「ENVG-B 热像轮廓」）

- 显示内容：当前模式。
- 夜视管增益、夜视亮度、白磷夜视开关、目镜显示信息开关。
- 轮廓亮度、轮廓多少、草的轮廓强度。
- 「高级」栏里还有热像分辨率、气温、延迟等参数，一般不用动。

### 热像是怎么画的

- 热像用物体表面自己的明暗，人体按约 34 °C 标出，轮廓模式里另外画草的轮廓。
- 和真实热像一样：窗户玻璃不透，画成一块均匀的面；墙上的字、涂鸦等贴花不出现在热像里。
- 按真实的方式画：在夜视仪的热像物镜处放一台相机，按传感器的分辨率（每度 16 像素）拍场景，朝向和眼睛一致。近处的东西出现在物镜看到的位置，所以枪上的瞄具挡住热像的地方比挡住眼睛的地方偏下；瞄具的倍率不会放大热像。F12 高级里「热像从物镜位置拍（真实画法）」可以关掉，关掉后热像取自眼睛这台相机。
- 瞄具会在热像上挖洞：从物镜看过去，视线穿过红点、全息或倍镜的玻璃、从瞄具前端穿出去的地方，只显示夜视画面；瞄具自己的罩子和镜筒内壁保留热像，挡在瞄具前面的手、头盔也保留自己的热像。
- 枪和手只用当前一帧的热像，晃枪时不会拖影。

## 模型来源

- **ENVG-B（AN/PSQ-42）和眼罩**：CGTrader 上 durchacht（https://www.cgtrader.com/designers/durchacht）制作的 AN/PSQ-42 Night Vision Goggles 模型：https://www.cgtrader.com/3d-models/military/other/an-psq-42-night-vision-goggles

模型按其授权用于免费模组，只以游戏资源包的形式发布，不包含原始模型文件。感谢模型作者 durchacht。

Model: AN/PSQ-42 Night Vision Goggles by durchacht on CGTrader (links above). Used for a free mod, shipped only as game bundles; the source model is not included.

## 安装

把发布的压缩包解压到 SPT 文件夹，会得到 `SPT_Runtime/user/mods/UstAchae-ENVGB`（服务端）和 `BepInEx/plugins/JustachaeENVGB`（客户端）。

- 配合 **K-3PS-C 1.9.0 beta 1 或更新版本**，并打开它的总开关：ENVG-B 像其他夜视仪一样通过目镜成像，双眼合并，支架可调，两个目镜可以各自掰起；热像画在右目镜里，大小和位置按眼睛实际看到的镜片来定。
- 用旧版 K-3PS-C（最后一个正式版是 1.8.2）或不装：ENVG-B 用游戏原版的全屏夜视，加上本模组的白磷效果，热像铺满整个屏幕。

Works on its own. With K-3PS-C 1.9.0 beta 1 or later installed and its master switch on, the device is imaged through its eyepieces like the other night vision goggles, and the thermal picture is drawn into the right eyepiece. With an older K-3PS-C or without it, the ENVG-B uses the game's own full-screen night vision, its white phosphor and the thermal picture laid over the whole screen.

## 仓库（开发用）

- `item/`: model pipeline and Unity bundle build
- `server/`: SPT server mod (item template, trader)
- `client/`: BepInEx plugin (thermal outline and fusion, symbology, pods)
- `package.py`: the release zip from the built outputs (checks server and client carry one version)

The purchased model sources are never committed (see 模型来源 above).
