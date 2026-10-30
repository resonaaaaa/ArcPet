using System;

namespace ArcPet.Behavior
{
    public sealed class PetLayout
    {
        public const double BaseSize = 320;
        public const double MinimumScale = 0.4;
        public const double MaximumScale = 2.0;
        public const double ResizeDistancePerScale = 240;
        public double Scale { get; private set; }
        public double Left { get; private set; }
        public double Top { get; private set; }
        public bool IsLocked { get; set; }
        public double Size { get { return BaseSize * Scale; } }
        public PetLayout(double left, double top) : this(left,top,1) { }
        public PetLayout(double left,double top,double scale)
        {
            if(Double.IsNaN(left)||Double.IsInfinity(left)||Double.IsNaN(top)||Double.IsInfinity(top))throw new ArgumentException("Invalid position.");
            Left=left;Top=top;Scale=Double.IsNaN(scale)||Double.IsInfinity(scale) ? 1 : Math.Max(MinimumScale,Math.Min(MaximumScale,scale));
        }
        public void MoveTo(double left, double top)
        { if (!IsLocked) { Left = left; Top = top; } }
        public void ResizeFrom(double initialScale, double initialPointerY, double pointerY, double footX, double footY)
        {
            if (IsLocked) return;
            Scale = Math.Max(MinimumScale, Math.Min(MaximumScale, initialScale + (initialPointerY - pointerY) / ResizeDistancePerScale));
            Left = footX - Size * 135 / BaseSize;
            Top = footY - Size * 292 / BaseSize;
        }
    }
}
