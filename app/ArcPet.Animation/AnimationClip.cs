using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ArcPet.Animation
{
    public sealed class AnimationFrame
    {
        public string AssetPath { get; private set; }
        public int DurationMilliseconds { get; private set; }
        public AnimationFrame(string assetPath, int durationMilliseconds)
        {
            if (String.IsNullOrWhiteSpace(assetPath)) throw new ArgumentException("A frame needs an asset path.");
            if (durationMilliseconds <= 0) throw new ArgumentOutOfRangeException("durationMilliseconds");
            AssetPath = assetPath;
            DurationMilliseconds = durationMilliseconds;
        }
    }

    public sealed class AnimationClip
    {
        private readonly long[] ends;
        public string Id { get; private set; }
        public bool Loop { get; private set; }
        public ReadOnlyCollection<AnimationFrame> Frames { get; private set; }
        public ReadOnlyCollection<int> HandoffFrames { get; private set; }
        public long DurationMilliseconds { get { return ends[ends.Length - 1]; } }
        public AnimationClip(string id, bool loop, IEnumerable<AnimationFrame> frames, IEnumerable<int> handoffFrames = null)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentException("A clip needs an ID.");
            if (frames == null) throw new ArgumentNullException("frames");
            var list = new List<AnimationFrame>(frames);
            if (list.Count == 0 || list.Contains(null)) throw new ArgumentException("A clip needs valid frames.");
            Id = id; Loop = loop; Frames = list.AsReadOnly();
            var markers=new List<int>(handoffFrames ?? new int[0]);
            foreach(int marker in markers)if(marker<0 || marker>=list.Count)throw new ArgumentOutOfRangeException("handoffFrames");
            markers.Sort();HandoffFrames=markers.AsReadOnly();
            ends = new long[list.Count];
            long total = 0;
            for (int i = 0; i < list.Count; i++) ends[i] = total = checked(total + list[i].DurationMilliseconds);
        }
        public long NextHandoff(long elapsedMilliseconds)
        {
            long elapsed=Math.Max(0,elapsedMilliseconds),cycle=elapsed/DurationMilliseconds,offset=elapsed%DurationMilliseconds;
            if(HandoffFrames.Contains(GetFrameIndex(elapsed)))return elapsed;
            foreach(int index in HandoffFrames)
            {
                long start=index==0 ? 0 : ends[index-1];
                if(start>=offset)return cycle*DurationMilliseconds+start;
            }
            return (cycle+1)*DurationMilliseconds;
        }
        public int GetFrameIndex(long elapsedMilliseconds)
        {
            long elapsed = Math.Max(0, elapsedMilliseconds);
            if (Loop) elapsed %= DurationMilliseconds;
            else if (elapsed >= DurationMilliseconds) return Frames.Count - 1;
            int low = 0, high = ends.Length - 1;
            while (low < high)
            {
                int mid = low + (high - low) / 2;
                if (elapsed < ends[mid]) high = mid; else low = mid + 1;
            }
            return low;
        }
    }
}
