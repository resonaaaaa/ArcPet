using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ArcPet.Animation
{
    public interface IAnimationCatalog { AnimationClip Load(string id); }
    public sealed class JsonAnimationCatalog : IAnimationCatalog
    {
        private readonly string root;
        public JsonAnimationCatalog(string rootDirectory) { root = Path.GetFullPath(rootDirectory); }
        private static T Read<T>(string path)
        {
            using (var stream = File.OpenRead(path))
                return (T)new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).ReadObject(stream);
        }
        private static string ResolveInside(string directory, string relative)
        {
            if (String.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) throw new InvalidDataException("Expected a relative asset path.");
            string parent = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(Path.Combine(parent, relative));
            if (!path.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Asset path escapes its directory.");
            return path;
        }
        public AnimationClip Load(string id)
        {
            var manifest = Read<ManifestData>(Path.Combine(root, "manifest.json"));
            ManifestAction action;
            if (manifest == null || manifest.Actions == null || !manifest.Actions.TryGetValue(id, out action) || action == null)
                throw new InvalidDataException("Animation ID not found: " + id);
            string clipPath = ResolveInside(root, action.Path);
            var data = Read<ClipData>(clipPath);
            if (data == null || data.Frames == null || data.Frames.Count != data.FrameCount || data.Frames.Count == 0)
                throw new InvalidDataException("Invalid animation frame count.");
            var frames = new List<AnimationFrame>();
            foreach (var item in data.Frames)
            {
                if (item == null || item.Duration <= 0) throw new InvalidDataException("Invalid frame duration.");
                string asset = ResolveInside(Path.GetDirectoryName(clipPath), item.File);
                if (!File.Exists(asset)) throw new FileNotFoundException("Animation frame is missing.", asset);
                frames.Add(new AnimationFrame(asset, item.Duration));
            }
            var clip = new AnimationClip(id, data.Loop, frames, data.HandoffFrames);
            if (clip.DurationMilliseconds != data.Duration) throw new InvalidDataException("Frame durations do not match the clip duration.");
            return clip;
        }
        public IDictionary<string,AnimationClip> LoadAll()
        {
            var manifest=Read<ManifestData>(Path.Combine(root,"manifest.json"));
            if(manifest==null || manifest.Actions==null)throw new InvalidDataException("Missing animation manifest.");
            var clips=new Dictionary<string,AnimationClip>();
            foreach(string id in manifest.Actions.Keys)clips.Add(id,Load(id));
            return clips;
        }
        [DataContract] private sealed class ManifestData
        { [DataMember(Name = "actions")] public Dictionary<string, ManifestAction> Actions { get; set; } }
        [DataContract] private sealed class ManifestAction
        { [DataMember(Name = "path")] public string Path { get; set; } }
        [DataContract] private sealed class ClipData
        {
            [DataMember(Name = "frame_count")] public int FrameCount { get; set; }
            [DataMember(Name = "duration_ms")] public long Duration { get; set; }
            [DataMember(Name = "loop")] public bool Loop { get; set; }
            [DataMember(Name = "frames")] public List<FrameData> Frames { get; set; }
            [DataMember(Name = "handoff_frames")] public List<int> HandoffFrames { get; set; }
        }
        [DataContract] private sealed class FrameData
        {
            [DataMember(Name = "file")] public string File { get; set; }
            [DataMember(Name = "duration_ms")] public int Duration { get; set; }
        }
    }
}
