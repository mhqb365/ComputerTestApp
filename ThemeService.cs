using System;
using System.Windows.Controls;
using System.Windows;
using HandyControl.Data;
using HandyControl.Themes;

namespace ComputerTestApp
{
    internal static class ThemeService
    {
        public static bool IsDark { get; private set; }

        public static void Initialize()
        {
            SetDarkMode(UserSettings.Default.Theme == "dark", false);
        }

        public static void SetDarkMode(bool isDark, bool save = true)
        {
            var skinType = isDark ? SkinType.Dark : SkinType.Default;
            var dictionaries = Application.Current.Resources.MergedDictionaries;
            var skinIndex = -1;
            for (var index = 0; index < dictionaries.Count; index++)
            {
                if (dictionaries[index].Source?.OriginalString.IndexOf("/Themes/Skin", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    skinIndex = index;
                    break;
                }
            }

            var skin = isDark ? "SkinDark.xaml" : "SkinDefault.xaml";
            var dictionary = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/HandyControl;component/Themes/{skin}", UriKind.Absolute)
            };

            if (skinIndex >= 0)
            {
                dictionaries.RemoveAt(skinIndex);
                dictionaries.Insert(skinIndex, dictionary);
            }
            else
            {
                dictionaries.Insert(0, dictionary);
            }

            foreach (Window window in Application.Current.Windows)
            {
                Theme.SetSkin(window, skinType);
                RefreshStyles(window);
            }

            IsDark = isDark;
            if (!save) return;

            UserSettings.Default.Theme = isDark ? "dark" : "light";
            UserSettings.Default.Save();
        }

        private static void RefreshStyles(DependencyObject element)
        {
            if (element is Control control)
            {
                var style = control.Style;
                control.Style = null;
                control.Style = style;
            }

            var childrenCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(element);
            for (var index = 0; index < childrenCount; index++)
            {
                RefreshStyles(System.Windows.Media.VisualTreeHelper.GetChild(element, index));
            }
        }
    }
}
