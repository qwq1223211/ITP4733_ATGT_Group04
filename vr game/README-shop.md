# 修车店场景（Shop Shell）

## 这是什么

- 一个**静态的修车店室内空壳**：地面、墙、天花板、卷帘门、窗、灯、地面标线、店招、排水沟、电箱。
- **没有**车、引擎、扳手、电脑、工作台、货架、轮胎、举升台等设备/道具（按需求不做）。
- 场景：`Assets/Scenes/VR game.unity`
- 店面：`Assets/Prefabs/Shop/ShopShell.prefab` 的实例，场景里显示为 `ShopRoot`。
- 店里所有东西都是场景/prefab 里的真实物体，**没有运行时生成店面的脚本**。
- Build Settings 已把 `VR game.unity` 放在第一个。

Unity 版本：`2022.3.62f3c1`（URP 14.0.12，XR Interaction Toolkit 3.1.2）。

## 嵌套 prefab 结构

```
Assets/Prefabs/Shop/
├─ ShopShell.prefab                 # 场景里的店面实例（ShopRoot）
│  ├─ 00_Architecture
│  │  ├─ Floor
│  │  ├─ Walls (Wall_North/West/East/South_*)
│  │  ├─ Trim (Baseboards)
│  │  ├─ Ceiling
│  │  └─ Trusses (Truss.prefab ×4)
│  ├─ 01_Openings
│  │  ├─ RollerDoor (RollerDoor.prefab)
│  │  └─ Windows (Window.prefab ×2)
│  ├─ 02_Lighting
│  │  ├─ Fixtures (CeilingLight.prefab ×4)
│  │  └─ FillLights (FillLight_West / FillLight_East)
│  ├─ 03_Details
│  │  ├─ FloorMarkings
│  │  ├─ Drain_Inset / Drain_Bar_01..05
│  │  ├─ Electrical (Panel_A/B, Conduit_A/B)
│  │  └─ Signage (Sign_Panel, Sign_Text "GARAGE 04")
│  └─ 04_Zones
│     ├─ Zone_Bay / Mount_Lift
│     ├─ Zone_ToolWall / Mount_ToolWall + Mount_Workbench
│     └─ Zone_Utility / Mount_Utility
├─ RollerDoor.prefab
├─ Window.prefab
├─ CeilingLight.prefab
└─ Truss.prefab
```

- 改所有灯：改 `CeilingLight.prefab`。
- 改所有窗：改 `Window.prefab`。
- 改门：改 `RollerDoor.prefab`。
- 改桁架：改 `Truss.prefab`。
- 改店体：打开 `ShopShell.prefab` 的 Prefab Mode。

## 尺寸与坐标

房间 12m(X) × 9m(Z) × 4m(Y)，墙厚 0.2m，内表面 x=±5.9、z=±4.4，地面顶面 y=0。

| 物体 | 位置 / 尺寸 |
|---|---|
| 地板 | (0,-0.1,0)，12×0.2×9 |
| 天花板 | (0,4.1,0)，12×0.2×9 |
| 卷帘门 | (0,1.6,-4.4)，3.6×3.2m，8 条门板 + 导轨 + 门框 |
| 东墙窗 1/2 | (5.87,2.4,-2.0) / (5.87,2.4,2.0)，1.6×1.2m |
| 顶灯 | y=3.4，X=±3，Z=±2 |
| 桁架 | y=3.6，z=-3.3 / -1.1 / 1.1 / 3.3 |
| 店招 | (0,3.5,-4.33)，朝 +Z（店内方向） |
| 出生点 / XR Rig | (0,0,-3.4)，朝 +Z |
| 地面标线 | y=0.005，关闭碰撞 |
| 排水沟 | (0,0.002,0.5)，关闭碰撞 |

## 预留挂点（不可见，只有 Trigger BoxCollider）

这些是给后续扩展用的，**不渲染、不挡路**：

| Zone | Mount | 大小 (X,Y,Z) | 位置 | 用途 |
|---|---|---|---|---|
| Zone_Bay | Mount_Lift | 2.6×0.4×5.2 | (0,0.2,0) | 未来举升台 / 车 |
| Zone_ToolWall | Mount_ToolWall | 0.12×2.2×3.2 | (-5.82,1.6,0) | 西墙工具墙 |
| Zone_ToolWall | Mount_Workbench | 0.75×0.9×2.4 | (-5.45,0.45,0) | 工具墙下方工作台 |
| Zone_Utility | Mount_Utility | 0.6×2.0×1.0 | (5.45,1.0,3.6) | 东南角货架/设备 |

## 后续怎么加东西

### 示例：加一面工具墙

1. 双击 `Assets/Prefabs/Shop/ShopShell.prefab`，进入 Prefab Mode。
2. 选中 `04_Zones/Zone_ToolWall`。
3. 在 `Mount_ToolWall` 的位置新建一个 `ToolWall_Root`（可以作为 `Zone_ToolWall` 的子物体，也可以直接挂到 `Mount_ToolWall` 下面）。
4. 工具墙按 **3.2m 宽 × 2.2m 高** 做，贴西墙（x≈-5.82）；洞洞板、挂钩、工具都放在 `ToolWall_Root` 下。`Mount_ToolWall` 的 BoxCollider 只用来对位，不要当碰撞墙。
5. 保存 prefab。场景里的 `ShopRoot` 实例会自动同步。
6. 如果工具墙以后要复用，把它另存成 `ToolWall.prefab` 再嵌套进去。

### 加工作台 / 举升台 / 货架

- 工作台：放在 `Zone_ToolWall/Mount_Workbench`（2.4m 长 × 0.9m 深 × 0.9m 高）。
- 举升台 / 车：放在 `Zone_Bay/Mount_Lift`（2.6×0.4×5.2）。
- 货架 / 设备：放在 `Zone_Utility/Mount_Utility`（0.6×2.0×1.0）。

建议把新物体做成 prefab，再嵌到对应 Zone 下；这样以后改一件就全店同步。挂点本身可以保留，作为对位参考。

## 灯光与 URP 注意

- 当前活动 URP 资产：`Assets/Settings/Project Configuration/Performance URP Config.asset`。
- 已把 **Additional Lights** 打开：`m_AdditionalLightsRenderingMode: 2`（PerPixel）、`m_AdditionalLightsPerObjectLimit: 8`、Additional Light Shadows 打开。
- 如果把这个资产换回默认的“Additional Lights = Disabled”，店内的顶灯/补光会全部不亮，只剩环境光——这是之前店里发黑的原因。
- 顶灯 prefab：Spot，intensity 220，range 16，spotAngle 115；`Fixture_03/04` 的阴影已关，只有 01/02 开 Soft。
- 补光：Point，intensity 40，range 14。
- 环境光：Flat，颜色 (0.50,0.52,0.55)，intensity 1.2；fog 关。
- 共 6 盏实时灯（4 顶灯 + 2 补光），0 盏方向光。

## 材质

`Assets/Materials/Shop/` 下 10 个共享 URP Lit/Unlit 材质：

`M_Concrete`、`M_WallLower`、`M_WallUpper`、`M_Trim`、`M_MetalDark`、`M_Galvanized`、`M_DoorMetal`、`M_WindowGlow`、`M_EmissiveWhite`、`M_YellowPaint`。

改共享材质会影响全店（包括嵌套 prefab 的实例）。

## 验证记录

- `unity_scene_validate`：0 error；3 个 warning 都来自 XR 模板自带空挂点（`Gaze Stabilized Attach`、左右 `Controller Stabilized Attach`），与店面无关。
- `perf_audit`：196 个对象（含 XR Rig）、14,816 三角面、6 盏实时灯、1 个相机、0 盏方向光。
- 截图：`Screenshots/shop_wide.png`、`shop_reverse.png`、`shop_door.png`、`shop_windows.png`。
- 工程 `Assets/` 下没有自定义运行时脚本；店面没有生成器。

## 不做（本版）

车、引擎、扳手、电脑、工作台、货架、轮胎、油桶、举升台、海报、挂钟、灭火器；交互、UI、金钱、任务、音效、存档、试驾。等下一阶段再加。

## 备注

- `Packages/com.local.unity-agent` 是 DSH 的 Unity 编辑器桥（开发工具），不是店面的运行时代码；删掉它店面仍然能打开，但 DSH 的 `unity_*` 工具会连不上。
- `Assets/moodle/` 是之前课程素材，本场景没有使用。
