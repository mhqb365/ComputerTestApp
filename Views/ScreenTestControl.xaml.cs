using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ComputerTestApp.Views
{
    public partial class ScreenTestControl : UserControl
    {
        private Color[] testColors = new Color[] { Colors.White, Colors.Black, Colors.Red, Colors.Green, Colors.Blue };

        public ScreenTestControl()
        {
            InitializeComponent();
        }

        private void StartTest_Click(object sender, RoutedEventArgs e)
        {
            int currentColorIndex = 0;
            var testWindow = new Window
            {
                WindowStyle = WindowStyle.None,
                WindowState = WindowState.Maximized,
                Topmost = true,
                Background = new SolidColorBrush(testColors[currentColorIndex])
            };

            testWindow.MouseDown += (s, args) =>
            {
                currentColorIndex++;
                if (currentColorIndex >= testColors.Length)
                {
                    testWindow.Close();
                }
                else
                {
                    testWindow.Background = new SolidColorBrush(testColors[currentColorIndex]);
                }
            };

            testWindow.KeyDown += (s, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    testWindow.Close();
                }
            };

            testWindow.ShowDialog();
        }
    }
}
