using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArcPet.Animation;

namespace ArcPet.Desktop
{
    internal sealed class FrameCache
    {
        private readonly Dictionary<string, BitmapSource> images = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<BitmapSource,Rect> visible=new Dictionary<BitmapSource,Rect>();
        public FrameCache(IEnumerable<AnimationClip> clips)
        {
            var identical=new Dictionary<string,BitmapSource>();
            foreach (var clip in clips)
            foreach (var frame in clip.Frames)
            {
                if (images.ContainsKey(frame.AssetPath)) continue;
                string hash;
                using(var sha=SHA256.Create())using(var stream=File.OpenRead(frame.AssetPath))hash=Convert.ToBase64String(sha.ComputeHash(stream));
                BitmapSource shared;if(identical.TryGetValue(hash,out shared)){images.Add(frame.AssetPath,shared);continue;}
                var image = new BitmapImage();
                image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(frame.AssetPath, UriKind.Absolute); image.EndInit();
                image.Freeze(); images.Add(frame.AssetPath, image);identical.Add(hash,image);
            }
        }
        public BitmapSource Get(string path) { return images[path]; }
        public bool IsOpaque(BitmapSource image,int x,int y)
        {
            if(image==null || x<0 || y<0 || x>=image.PixelWidth || y>=image.PixelHeight)return false;
            var rgba=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);var pixel=new byte[4];
            rgba.CopyPixels(new Int32Rect(x,y,1,1),pixel,4,0);return pixel[3]>=64;
        }
        public Rect VisibleBounds(AnimationClip clip)
        {
            Rect result=Rect.Empty;
            foreach(var frame in clip.Frames)
            {
                BitmapSource image=Get(frame.AssetPath);Rect bounds;
                if(!visible.TryGetValue(image,out bounds))
                {
                    var rgba=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);int w=rgba.PixelWidth,h=rgba.PixelHeight;
                    var bytes=new byte[w*h*4];rgba.CopyPixels(bytes,w*4,0);int x0=w,y0=h,x1=-1,y1=-1;
                    for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(bytes[(y*w+x)*4+3]>=64)
                    {x0=Math.Min(x0,x);y0=Math.Min(y0,y);x1=Math.Max(x1,x);y1=Math.Max(y1,y);}
                    bounds=x1>=x0 ? new Rect(x0,y0,x1-x0+1,y1-y0+1) : Rect.Empty;visible.Add(image,bounds);
                }
                result.Union(bounds);
            }
            return result;
        }
    }
}
