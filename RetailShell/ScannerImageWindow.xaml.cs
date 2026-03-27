using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace RetailShell
{
    public partial class ScannerImageWindow : Window
    {
        private string? _imageFile = null; // nullable to avoid non-nullable warning

        public ScannerImageWindow()
        {
            InitializeComponent();
        }

        // Load the image to display
        public void LoadImage(string imageFile)
        {
            _imageFile = imageFile;

            try
            {
                var bitmap = new BitmapImage(new Uri(imageFile, UriKind.Absolute));
                ImgScanner.Source = bitmap;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load scanner image: {ex.Message}");
            }
        }

        // Close window on Escape
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                this.Close();
        }

        // Optional: close window if clicked anywhere
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            this.Close();
        }

        // Fade-in animation
        public void FadeIn()
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(500));
            this.BeginAnimation(OpacityProperty, fade);
        }
    }
}