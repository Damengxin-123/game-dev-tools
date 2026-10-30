# MCP 服务与关卡设计

软件提供本机 Streamable HTTP MCP 服务。在左侧打开 **MCP 服务**，点击 **启动服务**，再点击 **复制连接配置**，将配置加入支持该传输方式的 AI 客户端。连接地址默认为 `http://127.0.0.1:24680/mcp`，需同时配置软件提供的 `Authorization: Bearer ...` 请求头。

服务需要软件保持运行。默认手动启动，也可以勾选自动开启。端口可以修改；修改前先停止服务。访问密钥可以重新生成，生成后需要更新客户端配置。仅监听本机回环地址，不需要管理员权限，不对局域网开放。

## 功能分类

目前提供“关卡设计”分类，分为读取、绘制编辑、预览三组。停用分类后不再公开这些工具，并拒绝调用。客户端可能缓存工具列表，切换分类后应重新连接。后续模块使用自己的前缀扩展工具目录。

| 工具 | 功能 |
| --- | --- |
| `level.get_document` | 当前标题、文档版本、对象总数、范围、单位、草稿及未保存状态；可选全部对象 |
| `level.list_objects` | 按名称、预设、类型查找，分页返回 ID、图层、节点数量和边界 |
| `level.get_objects` | 按 ID 获取完整顶点、颜色、尺寸和预设 |
| `level.get_presets` | 查询全部预设及默认样式 |
| `level.apply` | 原子批量创建、修改、删除；一次调用对应一次撤销 |
| `level.history` | 撤销或重做完整操作 |
| `level.view` | 选中对象、查看完整关卡 |
| `level.capture` | 返回完整关卡的 PNG 图像内容，AI 可以直接看图 |

## 让 AI 绘图

可以向已连接的 AI 说：

> 读取当前关卡，找到冒险者公会，在其南侧画一个宽 12.00 米、长 18.00 米的绿化花园，并添加 NPC 接待员；完成后获取关卡图像检查布局。

AI 应先调用 `level.get_document` 获取 `revision`，需要预设时查询 `level.get_presets`，再调用 `level.apply`。绘图参数例子：

```json
{
  "expectedRevision": "从当前文档读取的 revision",
  "operations": [
    {
      "action": "create",
      "object": {
        "preset": "建筑",
        "name": "新建商铺",
        "points": [[10.00, 10.00], [22.00, 10.00], [22.00, 18.00], [10.00, 18.00]]
      }
    }
  ]
}
```

坐标是米制世界坐标，X 向右、Y 向下，界面显示两位小数、文件保留双精度。颜色可用 `#RRGGBB`，含透明度时使用旧工程的 **`#AARRGGBB`** 顺序。

`create` 从预设默认样式开始，ID 由软件分配并返回。`update` 必须指定 `id`，只覆盖提供的字段；修改 `points` 会替换该对象全部节点。`delete` 的 `object` 只包含 `id`。批量操作按顺序应用，任何一项失败，整个批次均不生效。最多128项操作，单对象最多2048个节点，整个批次最多8192个节点。

文档版本不一致、用户正在绘制草稿或拖动时，AI 修改会被拒绝，应重新读取并等待用户完成操作。AI 的修改直接反映到当前画布并进入撤销历史，**不会自动覆盖磁盘工程**；保存仍使用软件原有保存入口。

`level.capture` 接受 `width`（256至2048，默认1280），返回标准 MCP image 内容及对应文档版本。截图包含完整正式图形，不含网格和控制点；软件必须运行在图形模式下。图像生成使用内部临时文件，请求完成后清理。

## 协议与验证

实现无状态 JSON-RPC MCP Streamable HTTP；协商支持 `2025-11-25`、`2025-06-18` 和 `2025-03-26`。POST 使用 JSON 响应，通知返回202；不提供服务器主动 SSE 消息，GET 返回405。请求带 `Accept: application/json, text/event-stream` 和 `Content-Type: application/json`。不需要会话 ID。

网络处理与 Godot 主线程分离，所有文档操作在主线程串行执行；服务停止会取消尚未执行的请求。页面显示最近调用状态。AI 输入只按工具参数解释，不执行代码，也不开放任意路径文件操作。

```powershell
./验证.ps1 -Mcp
./验证.ps1 -Mcp -Visual
```

验证使用真实 HTTP 覆盖协议初始化、认证、Origin、工具分类开关、对象创建/读取/修改/删除、批次失败回滚、版本竞争、撤销重做和图形模式 PNG 内容。

协议依据：[官方传输规范](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports)、[官方工具规范](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)。

## 物品配置分类

同一 MCP 服务中增加独立“物品配置”开关，工具名称使用 `item.` 前缀。沿用套件服务的端口和访问令牌，不启动旧 ItemStudio 的 18765 服务。旧客户端需改用当前页面生成的连接配置和下表工具名称。

| 工具 | 用途 |
| --- | --- |
| `item.get_catalog` | 读取分类、稀有度、角色属性、效果类型和新建示例 |
| `item.get_snapshot` | 完整业务 JSON，包括扩展字段与来源信息 |
| `item.list_records` | 按 `kind`、`category`、`search` 搜索，支持 `offset`、`limit` 分页 |
| `item.get_record` | 按 `kind` 和 `id` 读取完整记录 |
| `item.save_record` | 新增或更新；新物品/词条自动分配标识，更新仅覆盖提供的字段 |
| `item.delete_record` | 删除未被其他记录引用的配置 |
| `item.edit_category` | `add`、`rename`、`delete` 分类操作，联动下级记录 |
| `item.import_resources` | 从本机绝对路径数组 `paths` 导入图标到 `category` |
| `item.get_image` | 以 MCP 图像内容返回已登记资源的缩略图 |

`kind` 为 `items`（物品）、`affixes`（词条）或 `assets`（图标）。写入必须提供读取结果中的 `expectedRevision` 整数；版本过期或人工草稿尚未保存时拒绝写入。成功写入立即保存到当前物品目录并刷新界面，关卡设计的内存编辑/用户保存行为不变。文件锁、外部改动检测、引用检查与原子备份均复用原生界面业务层。

物品 MCP 不直接修改虚幻资产；完整数据仍可用原 ItemStudio 打开。旧服务的 `studio://` 资源地址不作为本套件协议地址，图像使用 `item.get_image` 读取。