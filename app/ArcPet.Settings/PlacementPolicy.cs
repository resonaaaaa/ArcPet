using System;
using System.Collections.Generic;
using ArcPet.Behavior;

namespace ArcPet.Settings
{
    public sealed class DisplayArea
    {
        public string Name { get; private set; }
        public double Left { get; private set; }
        public double Top { get; private set; }
        public double Width { get; private set; }
        public double Height { get; private set; }
        public bool Primary { get; private set; }
        public DisplayArea(string name,double left,double top,double width,double height,bool primary)
        {
            if(!PetPreferences.Finite(left)||!PetPreferences.Finite(top)||!PetPreferences.Finite(width)||!PetPreferences.Finite(height)||width<=0||height<=0)
                throw new ArgumentException("Invalid display area.");
            Name=name;Left=left;Top=top;Width=width;Height=height;Primary=primary;
        }
    }
    public static class PlacementPolicy
    {
        private static double Clamp(double value,double low,double high){return Math.Max(low,Math.Min(Math.Max(low,high),value));}
        public static DisplayArea Choose(IList<DisplayArea> areas,double left,double top,double size)
        {
            if(areas==null||areas.Count==0)throw new ArgumentException("At least one display is required.");
            DisplayArea best=areas[0];double bestArea=-1;
            foreach(var area in areas)
            {
                double intersection=Math.Max(0,Math.Min(left+size,area.Left+area.Width)-Math.Max(left,area.Left))*
                    Math.Max(0,Math.Min(top+size,area.Top+area.Height)-Math.Max(top,area.Top));
                if(intersection>bestArea || (intersection==bestArea && area.Primary)){best=area;bestArea=intersection;}
            }
            return best;
        }
        public static PetPreferences Restore(PetPreferences preferences,IList<DisplayArea> areas)
        {
            if(areas==null||areas.Count==0)throw new ArgumentException("At least one display is required.");
            var value=preferences.Normalize();DisplayArea target=null;
            foreach(var area in areas)if(area.Name==value.Monitor){target=area;break;}
            bool known=target!=null;
            if(target==null)target=Choose(areas,value.Left ?? Double.MinValue/2,value.Top ?? Double.MinValue/2,PetLayout.BaseSize*value.Scale);
            double oldSize=PetLayout.BaseSize*value.Scale;
            value.Scale=Math.Max(PetLayout.MinimumScale,Math.Min(value.Scale,Math.Min(target.Width,target.Height)/PetLayout.BaseSize));
            double size=PetLayout.BaseSize*value.Scale;
            double left=value.Left ?? target.Left+target.Width-size-36;
            double top=value.Top ?? target.Top+target.Height-size-20;
            if(known && value.Left.HasValue && value.WorkWidth.HasValue &&
                (value.WorkLeft.Value!=target.Left || value.WorkTop.Value!=target.Top || value.WorkWidth.Value!=target.Width || value.WorkHeight.Value!=target.Height))
            {
                double rx=Clamp((left-value.WorkLeft.Value)/Math.Max(1,value.WorkWidth.Value-oldSize),0,1);
                double ry=Clamp((top-value.WorkTop.Value)/Math.Max(1,value.WorkHeight.Value-oldSize),0,1);
                left=target.Left+rx*Math.Max(0,target.Width-size);top=target.Top+ry*Math.Max(0,target.Height-size);
            }
            value.Left=Clamp(left,target.Left,target.Left+target.Width-size);
            value.Top=Clamp(top,target.Top,target.Top+target.Height-size);
            value.Monitor=target.Name;value.WorkLeft=target.Left;value.WorkTop=target.Top;value.WorkWidth=target.Width;value.WorkHeight=target.Height;
            return value;
        }
    }
}
