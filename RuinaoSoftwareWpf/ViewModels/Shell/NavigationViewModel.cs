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
