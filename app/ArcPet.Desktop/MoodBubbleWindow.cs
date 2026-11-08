using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArcPet.Behavior;

namespace ArcPet.Desktop
{
    // Separate owned, nonactivating, click-through window. Pet bounds, mouse
    // coordinates and persisted placement are unchanged by the space above it.
    internal sealed class MoodBubbleWindow : Window
    {
        private readonly MoodAssets data;
        private readonly Window pet;
        private readonly Canvas canvas=new Canvas();
        private readonly Dictionary<string,BitmapImage> icons=new Dictionary<string,BitmapImage>();
        private readonly Dictionary<string,UIElement> layers=new Dictionary<string,UIElement>();
        private readonly Image icon;
        private string renderedContent;
        private string timerText;
        private long timerStarted;
        internal bool TimerVisible { get { return timerText!=null; } }
        internal string TimerText { get { return timerText; } }
        internal void SetTimer(bool visible,long remaining,long now)
        {
            string text=visible ? PixelClock.Format(remaining) : null;
            if(text==timerText)return;
            if(timerText==null && text!=null)timerStarted=now;
            timerText=text;renderedContent=null;
            if(text!=null)icon.Source=PixelClock.Draw(text);
        }
        internal MoodBubbleController Controller {get;private set;}
        internal Canvas Artwork {get{return canvas;}}
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")]private static extern int GetWindowLong(IntPtr handle,int index);
        [DllImport("user32.dll",EntryPoint="SetWindowLongW")]private static extern int SetWindowLong(IntPtr handle,int index,int value);
        internal MoodBubbleWindow(Window owner,string assetRoot,IRandomSource random)
        {
            using(var stream=File.OpenRead(Path.Combine(assetRoot,"manifest.json")))
                data=(MoodAssets)new DataContractJsonSerializer(typeof(MoodAssets)).ReadObject(stream);
            if(data==null || data.Version!=1 || data.Clouds==null || data.Clouds.Count!=3 || data.Icons==null || data.Icon==null || data.Bounds==null)throw new InvalidDataException("Invalid mood assets.");
            var content=new List<MoodContent>();
            foreach(var item in data.Icons){content.Add(new MoodContent(item.Id,item.Category));icons.Add(item.Id,Load(assetRoot,"icons/"+item.Id+".png"));}
            Controller=new MoodBubbleController(content,random);
            pet=owner;Title="ArcPet mood overlay";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
            AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;
            Topmost=owner.Topmost;IsHitTestVisible=false;Focusable=false;
            canvas.Width=data.Bounds.Width;canvas.Height=data.Bounds.Height;
            var box=new Viewbox{Stretch=Stretch.Fill,Child=canvas,IsHitTestVisible=false};Content=box;
            var main=new Canvas{Width=data.Bounds.Width,Height=data.Bounds.Height,IsHitTestVisible=false};
            RenderOptions.SetEdgeMode(canvas,EdgeMode.Aliased);
            foreach(var layer in data.Clouds)
            {
                if(layer.Fade<=0 || layer.Width<=0 || layer.Height<=0)throw new InvalidDataException("Invalid mood layer.");
                var image=MakeImage(Load(assetRoot,layer.File),layer.Width,layer.Height,layer.X,layer.Y);
                if(layer.Id=="main"){main.Children.Add(image);layers.Add(layer.Id,main);}
                else{canvas.Children.Add(image);layers.Add(layer.Id,image);}
            }
            if(!layers.ContainsKey("small") || !layers.ContainsKey("medium") || !layers.ContainsKey("main"))throw new InvalidDataException("Missing cloud layer.");
            icon=MakeImage(null,data.Icon.Width,data.Icon.Height,data.Icon.X,data.Icon.Y);main.Children.Add(icon);canvas.Children.Add(main);
            SourceInitialized+=delegate
            {
                IntPtr handle=new WindowInteropHelper(this).Handle;
                // WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW.
                SetWindowLong(handle,-20,GetWindowLong(handle,-20)|0x20|0x08000000|0x80);
            };
            Controller.SetAvailable(owner.IsVisible,0);
        }
        private static BitmapImage Load(string root,string relative)
        {
            string parent=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string path=Path.GetFullPath(Path.Combine(parent,relative));
            if(!path.StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Mood asset escapes its directory.");
            var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(path);image.EndInit();image.Freeze();return image;
        }
        private Image MakeImage(ImageSource source,double width,double height,double x,double y)
        {
            var image=new Image{Source=source,Width=width,Height=height,Stretch=Stretch.Fill,IsHitTestVisible=false,SnapsToDevicePixels=true};
            RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(image,x-data.Bounds.X);Canvas.SetTop(image,y-data.Bounds.Y);return image;
        }
        internal void Render(long now)
        {
            if(!pet.IsVisible || (!TimerVisible && !Controller.IsVisible)){if(IsVisible)Hide();return;}
            if(Owner==null)Owner=pet;
            if(!TimerVisible && renderedContent!=Controller.ContentId){icon.Source=icons[Controller.ContentId];renderedContent=Controller.ContentId;}
            double scale=pet.Width/320.0;
            Left=pet.Left+data.Bounds.X*scale;Top=pet.Top+data.Bounds.Y*scale;
            Width=data.Bounds.Width*scale;Height=data.Bounds.Height*scale;Topmost=pet.Topmost;
            foreach(var layer in data.Clouds)layers[layer.Id].Opacity=TimerVisible ?
                Math.Max(0,Math.Min(1,(now-timerStarted-layer.Start)/(double)layer.Fade)) : Controller.Opacity(now,layer.Start,layer.Fade);
            if(!IsVisible)Show();
        }
        [DataContract]private sealed class MoodAssets
        {
            [DataMember(Name="version")]public int Version {get;set;}
            [DataMember(Name="icons")]public List<MoodIcon> Icons {get;set;}
            [DataMember(Name="clouds")]public List<MoodLayer> Clouds {get;set;}
            [DataMember(Name="icon")]public MoodRect Icon {get;set;}
            [DataMember(Name="bounds")]public MoodRect Bounds {get;set;}
        }
        [DataContract]private sealed class MoodIcon
        {
            [DataMember(Name="id")]public string Id {get;set;}
            [DataMember(Name="category")]public string Category {get;set;}
        }
        [DataContract]private class MoodRect
        {
            [DataMember(Name="x")]public double X {get;set;}
            [DataMember(Name="y")]public double Y {get;set;}
            [DataMember(Name="width")]public double Width {get;set;}
            [DataMember(Name="height")]public double Height {get;set;}
        }
        [DataContract]private sealed class MoodLayer:MoodRect
        {
            [DataMember(Name="id")]public string Id {get;set;}
            [DataMember(Name="file")]public string File {get;set;}
            [DataMember(Name="start_ms")]public int Start {get;set;}
            [DataMember(Name="fade_ms")]public int Fade {get;set;}
        }
    }
}
