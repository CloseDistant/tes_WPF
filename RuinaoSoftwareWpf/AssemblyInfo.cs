using System.Windows;

// ThemeInfo 告诉 WPF 主题资源字典的位置。
// - 第一个参数：主题特定资源字典位置，这里设为 None，表示不使用主题特定字典。
// - 第二个参数：通用资源字典位置，这里设为 SourceAssembly，表示在程序集内部查找。
[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,            // where theme specific resource dictionaries are located
                                                // (used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   // where the generic resource dictionary is located
                                                // (used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
