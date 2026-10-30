using System;
using ArcPet.Animation;

namespace ArcPet.Behavior
{
    public enum MovementMode { Off, Long, Short }
    public static class MovementOptions
    {
        public static MovementMode Parse(string value){return value=="long" ? MovementMode.Long : value=="short" ? MovementMode.Short : MovementMode.Off;}
        public static string Setting(MovementMode value){return value==MovementMode.Long ? "long" : value==MovementMode.Short ? "short" : "off";}
    }
    // Coordinates are supplied by the desktop adapter; no display or UI dependency.
    public sealed class TravelArea
    {
        public double MinimumLeft,MaximumLeft,Margin,RightMargin;
        public TravelArea(double min,double max,double margin):this(min,max,margin,margin){}
        public TravelArea(double min,double max,double leftMargin,double rightMargin)
        {MinimumLeft=min;MaximumLeft=max;Margin=leftMargin;RightMargin=rightMargin;}
        public bool Valid {get{return Finite(MinimumLeft)&&Finite(MaximumLeft)&&Finite(Margin)&&Finite(RightMargin)&&MaximumLeft>MinimumLeft;}}
        public static bool Finite(double value){return !Double.IsNaN(value)&&!Double.IsInfinity(value);}
        public double SafeMinimum {get{return MinimumLeft+Math.Min(Math.Max(0,Margin),(MaximumLeft-MinimumLeft)/4);}}
        public double SafeMaximum {get{return MaximumLeft-Math.Min(Math.Max(0,RightMargin),(MaximumLeft-MinimumLeft)/4);}}
    }
    public sealed class TravelStep
    {
        public double Left,Top;
        public bool Finished,DirectionChanged;
        public string Reason;
    }
    public sealed class HorizontalTravel
    {
        private readonly AnimationClip leftClip,rightClip;
        private double target,speed;
        private long lastUpdated,directionStarted;
        public bool IsActive {get;private set;}
        public int Direction {get;private set;}
        public string Stage {get;private set;}
        public MovementMode Mode {get;private set;}
        public double Left {get;private set;}
        public double Top {get;private set;}
        public double OriginLeft {get;private set;}
        public double OutboundDistance {get;private set;}
        public HorizontalTravel(AnimationClip left,AnimationClip right)
        {if(left==null||right==null)throw new ArgumentNullException();leftClip=left;rightClip=right;}
        public static bool CanStart(TravelArea area,double origin,double width)
        {
            return area!=null&&area.Valid&&TravelArea.Finite(origin)&&TravelArea.Finite(width)&&width>0&&
                origin>=area.MinimumLeft&&origin<=area.MaximumLeft&&
                Math.Max(area.SafeMaximum-origin,origin-area.SafeMinimum)>1;
        }
        public bool Start(MovementMode mode,TravelArea area,double origin,double top,double characterWidth,long now)
        {
            if(mode==MovementMode.Off || !TravelArea.Finite(top) || !CanStart(area,origin,characterWidth))return false;
            double toLeft=Math.Max(0,origin-area.SafeMinimum),toRight=Math.Max(0,area.SafeMaximum-origin);
            Direction=toRight>=toLeft ? 1 : -1;
            double room=Direction>0 ? toRight : toLeft;
            OutboundDistance=mode==MovementMode.Short ? Math.Min(5*characterWidth,room) : room;
            OriginLeft=Left=origin;Top=top;Mode=mode;Stage="outbound";IsActive=true;
            target=origin+Direction*OutboundDistance;speed=Math.Max(12,characterWidth*.8);
            lastUpdated=directionStarted=now;return true;
        }
        public void Stop(){IsActive=false;Stage="stopped";}
        private TravelStep Snapshot(bool finished,bool changed,string reason)
        {return new TravelStep{Left=Left,Top=Top,Finished=finished,DirectionChanged=changed,Reason=reason};}
        private TravelStep Finish(string reason){Stop();return Snapshot(true,false,reason);}
        private TravelStep TryTurn(long now)
        {
            AnimationClip clip=Direction>0 ? rightClip : leftClip;
            long elapsed=now-directionStarted;
            if(clip.NextHandoff(elapsed)>elapsed)return Snapshot(false,false,null);
            Direction=-Direction;Stage="return";directionStarted=now;
            return Snapshot(false,true,null);
        }
        public TravelStep Update(long now,TravelArea area)
        {
            if(!IsActive)return Snapshot(true,false,"inactive");
            if(area==null || !area.Valid)return Finish("display-unavailable");
            if(Left<area.MinimumLeft || Left>area.MaximumLeft)
            {Left=Math.Max(area.MinimumLeft,Math.Min(area.MaximumLeft,Left));return Finish("display-changed");}
            long delta=Math.Max(0,now-lastUpdated);lastUpdated=Math.Max(now,lastUpdated);
            if(delta>1000)return Finish("clock-gap"); // Never teleport after a suspended UI/system.
            if(Stage=="turn")
            {
                return TryTurn(now);
            }
            double destination;
            if(Stage=="return")destination=Math.Max(area.MinimumLeft,Math.Min(area.MaximumLeft,OriginLeft));
            else destination=Direction>0 ? Math.Min(target,area.SafeMaximum) : Math.Max(target,area.SafeMinimum);
            // A moving display boundary may already be behind our current position.
            if(Stage=="outbound" && Direction*(destination-Left)<0)destination=Left;
            double remaining=Direction*(destination-Left);
            double distance=Math.Min(Math.Max(0,remaining),speed*delta/1000.0);
            Left+=Direction*distance;
            if(remaining-distance>0.000001)return Snapshot(false,false,null);
            Left=destination;
            if(Stage=="return")return Finish(Math.Abs(Left-OriginLeft)<.000001 ? "returned" : "return-boundary");
            if(Mode==MovementMode.Long)return Finish("edge");
            Stage="turn";
            return TryTurn(now);
        }
    }
}
