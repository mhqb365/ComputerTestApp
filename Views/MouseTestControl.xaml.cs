using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ComputerTestApp.Views
{
    public partial class MouseTestControl : UserControl
    {
        public MouseTestControl()
        {
            InitializeComponent();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            this.Focus();
        }

        private void MouseArea_MouseDown(object sender, MouseButtonEventArgs e)
        {
            ClickDisplay.Text = $"Nhấn chuột: {e.ChangedButton}";
        }

        private void MouseArea_MouseUp(object sender, MouseButtonEventArgs e)
        {
            ClickDisplay.Text = $"Nhả chuột: {e.ChangedButton}";
        }

        private void MouseArea_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            string direction = e.Delta > 0 ? "Lên" : "Xuống";
            WheelDisplay.Text = $"Cuộn chuột: {direction} (Delta: {e.Delta})";
        }

        private void MouseArea_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(MouseArea);
            PositionDisplay.Text = $"X: {(int)pos.X} | Y: {(int)pos.Y}";
        }
    }
}
