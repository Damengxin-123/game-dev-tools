using Godot;
using System.Globalization;

namespace GameDevTools;

/// <summary>尺寸输入统一显示两位小数，保留原生数值解析和键盘操作。</summary>
public partial class DecimalSpinBox : SpinBox
{
    /// <summary>监听原生输入结束，在控件自身格式化之后补齐小数。</summary>
    public override void _Ready()
    {
        ValueChanged += _ => CallDeferred(nameof(FormatValue));
        GetLineEdit().FocusExited += () => CallDeferred(nameof(FormatValue));
        CallDeferred(nameof(FormatValue));
    }
    /// <summary>只修改显示字符串，不舍入持久化数据。</summary>
    public void FormatValue()
    {
        if (GetLineEdit().HasFocus()) return;
        GetLineEdit().Text = Value.ToString(Step >= 1 ? "F0" : "F2", CultureInfo.InvariantCulture) + (Suffix.Length > 0 ? " " + Suffix.Trim() : "");
    }
}
