using Godot;
using System;

namespace GameDevTools;

/// <summary>支持将已保存记录拖到分类树的原生列表。</summary>
public partial class ItemRecordList : ItemList
{
    /// <summary>将列表索引转换成稳定记录标识。</summary>
    public Func<int, string>? ResolveId { get; set; }
    /// <summary>为当前鼠标下的记录构造拖动数据。</summary>
    public override Variant _GetDragData(Vector2 atPosition)
    {
        int index = GetItemAtPosition(atPosition, true); string id = index >= 0 ? ResolveId?.Invoke(index) ?? "" : "";
        if (id == "") return default; SetDragPreview(new Label { Text = GetItemText(index), Modulate = Colors.Red });
        return new Godot.Collections.Dictionary { ["itemStudioRecord"] = id };
    }
}
