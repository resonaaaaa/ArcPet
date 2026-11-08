using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ArcPet.Desktop
{
    // A fixed 3x5 bitmap alphabet rendered with square pixels; no font dependency.
    internal static class PixelClock
    {
        private static readonly string[] Digits = {
            "111101101101111", "010110010010111", "111001111100111", "111001111001111",
            "101101111001001", "111100111001111", "111100111101111", "111001001001001",
            "111101111101111", "111101111001111" };
        internal static string Format(long remaining)
        {
            long seconds=(Math.Max(0,remaining)+999)/1000;
            return String.Format(CultureInfo.InvariantCulture,"{0:00}:{1:00}",seconds/60,seconds%60);
        }
        internal static ImageSource Draw(string text)
        {
            var group=new DrawingGroup();
            using(var dc=group.Open())
            {
                dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,64,64));
                var brush=new SolidColorBrush(Color.FromRgb(46,45,60));brush.Freeze();
                double x=6.5;
                foreach(char c in text)
                {
                    if(c==':')
                    {
                        dc.DrawRectangle(brush,null,new Rect(x,25,3,3));dc.DrawRectangle(brush,null,new Rect(x,31,3,3));x+=6;continue;
                    }
                    string pixels=Digits[c-'0'];
                    for(int i=0;i<15;i++)if(pixels[i]=='1')dc.DrawRectangle(brush,null,new Rect(x+i%3*3,22+i/3*3,3,3));
                    x+=12;
                }
            }
            group.Freeze();var image=new DrawingImage(group);image.Freeze();return image;
        }
    }
}
