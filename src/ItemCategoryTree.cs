using Godot;
using System;

namespace GameDevTools;

/// <summary>接收记录拖动并将有效分类交给工作区提交。</summary>
public partial class ItemCategoryTree : Tree
{
    /// <summary>检查记录和目标分类是否允许移动。</summary>
    public Func<string, string, bool>? CanMove { get; set; }
    /// <summary>移动记录的业务回调。</summary>
    public Action<string, string>? MoveRecord { get; set; }
    /// <summary>检查拖动来源和鼠标下的分类路径。</summary>
    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        DropModeFlags = (int)DropModeFlagsEnum.OnItem; if (data.VariantType != Variant.Type.Dictionary) return false;
        var map = data.AsGodotDictionary(); if (!map.ContainsKey("itemStudioRecord")) return false;
        string path = GetItemAtPosition(atPosition)?.GetMetadata(0).AsString() ?? "";
        return path.Length > 0 && CanMove?.Invoke(map["itemStudioRecord"].AsString(), path) == true;
    }
    /// <summary>松开鼠标后提交一次移动，由页面处理未保存提醒。</summary>
    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (!_CanDropData(atPosition, data)) return;
        MoveRecord?.Invoke(data.AsGodotDictionary()["itemStudioRecord"].AsString(), GetItemAtPosition(atPosition)!.GetMetadata(0).AsString());
    }
}
