namespace GameDevTools;

/// <summary>将物品功能登记到工具套件的独立页面。</summary>
public partial class MainUI
{
    /// <summary>创建与关卡画布无关的原生物品编辑页面。</summary>
    private void BuildItems() { var page = Page("items"); _itemStudio = new ItemStudioPage(); page.AddChild(_itemStudio); }
    /// <summary>验证时切换到物品页面以检查真实布局。</summary>
    public void ShowItemsForCheck() => ShowPage("items");
}
