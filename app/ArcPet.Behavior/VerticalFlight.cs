using System;

namespace ArcPet.Behavior
{
    public sealed class FlightStep
    {
        public double Left,Top;
        public bool Finished;
        public string Stage,Reason;
    }
    // Pure motion: never changes locking, random-action eligibility or preferences.
    public sealed class VerticalFlight
    {
        private long started,lastUpdated;
        private double topLimit;
        public bool IsActive {get;private set;}
        public double OriginLeft {get;private set;}
        public double OriginTop {get;private set;}
        public double Left {get;private set;}
        public double Top {get;private set;}
        public double TargetTop {get;private set;}
        public const int AscentMilliseconds=900, ApexMilliseconds=120, ReturnMilliseconds=800;
        public bool Start(double left,double top,double screenTop,long now)
        {
            if(!TravelArea.Finite(left)||!TravelArea.Finite(top)||!TravelArea.Finite(screenTop))return false;
            OriginLeft=Left=left;OriginTop=Top=top;topLimit=screenTop;
            TargetTop=Math.Min(top,screenTop+12);started=lastUpdated=now;IsActive=true;return true;
        }
        public void Stop(){IsActive=false;}
        public FlightStep Update(long now,double currentScreenTop)
        {
            string reason=null,stage="ascent";
            if(!IsActive)return new FlightStep{Left=OriginLeft,Top=OriginTop,Finished=true,Stage="finished",Reason="inactive"};
            if(!TravelArea.Finite(currentScreenTop) || currentScreenTop!=topLimit)reason="display-changed";
            else if(now-lastUpdated>1000)reason="clock-gap";
            lastUpdated=Math.Max(now,lastUpdated);long t=Math.Max(0,now-started);
            if(reason!=null || t>=AscentMilliseconds+ApexMilliseconds+ReturnMilliseconds)
            {IsActive=false;Top=OriginTop;stage="finished";reason=reason??"returned";}
            else if(t<AscentMilliseconds)
            {double u=t/(double)AscentMilliseconds;Top=OriginTop+(TargetTop-OriginTop)*u*u;}
            else if(t<AscentMilliseconds+ApexMilliseconds){Top=TargetTop;stage="apex";}
            else
            {
                double u=(t-AscentMilliseconds-ApexMilliseconds)/(double)ReturnMilliseconds;
                Top=TargetTop+(OriginTop-TargetTop)*(1-Math.Cos(Math.PI*u))/2;stage="return";
            }
            Left=OriginLeft;return new FlightStep{Left=Left,Top=Top,Finished=!IsActive,Stage=stage,Reason=reason};
        }
    }
}
