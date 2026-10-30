using System;
using System.Runtime.Serialization;
using ArcPet.Behavior;

namespace ArcPet.Settings
{
    [DataContract]
    public sealed class PetPreferences
    {
        [DataMember(Name="version",Order=0)] public int Version { get; set; }
        [DataMember(Name="scale",Order=1)] public double Scale { get; set; }
        [DataMember(Name="left",Order=2)] public double? Left { get; set; }
        [DataMember(Name="top",Order=3)] public double? Top { get; set; }
        [DataMember(Name="locked",Order=4)] public bool Locked { get; set; }
        [DataMember(Name="sleep_enabled",Order=5)] public bool SleepEnabled { get; set; }
        [DataMember(Name="visible",Order=6)] public bool Visible { get; set; }
        [DataMember(Name="topmost",Order=7)] public bool Topmost { get; set; }
        [DataMember(Name="monitor",Order=8)] public string Monitor { get; set; }
        [DataMember(Name="work_left",Order=9)] public double? WorkLeft { get; set; }
        [DataMember(Name="work_top",Order=10)] public double? WorkTop { get; set; }
        [DataMember(Name="work_width",Order=11)] public double? WorkWidth { get; set; }
        [DataMember(Name="work_height",Order=12)] public double? WorkHeight { get; set; }
        [DataMember(Name="movement_mode",Order=13)] public string Movement {get;set;}
        public PetPreferences() { SetDefaults(); }
        [OnDeserializing] private void OnDeserializing(StreamingContext context) { SetDefaults(); }
        private void SetDefaults() { Version=1;Scale=1;SleepEnabled=true;Visible=true;Topmost=true;Movement="off"; }
        public PetPreferences Copy() { return (PetPreferences)MemberwiseClone(); }
        public static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        public PetPreferences Normalize()
        {
            var value=Copy();
            value.Movement=MovementOptions.Setting(MovementOptions.Parse(value.Movement));
            value.Scale=Finite(value.Scale) ? Math.Max(PetLayout.MinimumScale,Math.Min(PetLayout.MaximumScale,value.Scale)) : 1;
            if(!value.Left.HasValue || !value.Top.HasValue || !Finite(value.Left.Value) || !Finite(value.Top.Value)) {value.Left=null;value.Top=null;}
            if(!value.WorkLeft.HasValue || !value.WorkTop.HasValue || !value.WorkWidth.HasValue || !value.WorkHeight.HasValue ||
               !Finite(value.WorkLeft.Value) || !Finite(value.WorkTop.Value) || !Finite(value.WorkWidth.Value) || !Finite(value.WorkHeight.Value) ||
               value.WorkWidth.Value<=0 || value.WorkHeight.Value<=0)
            {value.WorkLeft=null;value.WorkTop=null;value.WorkWidth=null;value.WorkHeight=null;}
            return value;
        }
    }
}
