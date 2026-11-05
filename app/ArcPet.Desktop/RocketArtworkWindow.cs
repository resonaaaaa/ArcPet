using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ArcPet.Desktop
{
    // Extended artwork may include fire below the original 320px pet bounds.
    // This passive window does not resize or replace the pet's input surface.
    internal sealed class RocketArtworkWindow:Window
    {
        private readonly Window pet;
        private readonly Image artwork=new Image{Stretch=Stretch.Fill,IsHitTestVisible=false};
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")]private static extern int GetWindowLong(IntPtr h,int i);
        [DllImport("user32.dll",EntryPoint="SetWindowLongW")]private static extern int SetWindowLong(IntPtr h,int i,int v);
        internal RocketArtworkWindow(Window pet)
        {
            this.pet=pet;Title="ArcPet rocket artwork";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
            AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Focusable=false;IsHitTestVisible=false;
            RenderOptions.SetBitmapScalingMode(artwork,BitmapScalingMode.HighQuality);Content=artwork;
            SourceInitialized+=delegate{IntPtr h=new WindowInteropHelper(this).Handle;SetWindowLong(h,-20,GetWindowLong(h,-20)|0x20|0x08000000|0x80);};
        }
        internal void Render(BitmapSource frame, int offsetX = 0, int offsetY = 0)
        {
            if(frame==null || !pet.IsVisible){if(IsVisible)Hide();return;}
            if(Owner==null)Owner=pet;
            double scale=pet.Width/320.0;Left=pet.Left+offsetX*scale;Top=pet.Top+offsetY*scale;Width=frame.PixelWidth*scale;Height=frame.PixelHeight*scale;Topmost=pet.Topmost;
            artwork.Source=frame;if(!IsVisible)Show();
        }
    }
}
