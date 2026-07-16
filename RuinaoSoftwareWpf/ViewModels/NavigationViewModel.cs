using System.Collections.ObjectModel;

namespace RuinaoSoftwareWpf;

/// <summary>
/// 左侧导航栏 ViewModel。
/// 维护一组 NavItem，并提供选中某一页的能力。
/// </summary>
public sealed class NavigationViewModel : ObservableObject
{
    /// <summary>导航项集合。XAML 左侧 ItemsControl 绑定它。</summary>
    public ObservableCollection<NavItem> Items { get; } = new();

    /// <summary>重新设置导航项。</summary>
    public void SetItems(IEnumerable<NavItem> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }
    }

    /// <summary>把指定页面对应的导航项设为选中，其他项设为未选中。</summary>
    public void Select(AppPage page)
    {
        foreach (var item in Items)
        {
            item.IsSelected = item.Page == page;
        }
    }
}

/// <summary>
/// 左侧导航栏的单一项。
/// </summary>
public sealed class NavItem : ObservableObject
{
    private string text;
    private bool isSelected;

    public NavItem(AppPage page, string text)
    {
        Page = page;
        this.text = text;
    }

    /// <summary>对应页面。</summary>
    public AppPage Page { get; }

    /// <summary>显示文字。</summary>
    public string Text
    {
        get => text;
        set => SetProperty(ref text, value);
    }

    /// <summary>是否被选中。用于高亮当前导航项。</summary>
    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}
