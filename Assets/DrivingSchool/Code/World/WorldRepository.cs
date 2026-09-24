using System;
using System.IO;
using System.Collections.Generic;
using DrivingSchool.Contracts;
using UnityEngine;
namespace DrivingSchool.World
{
    public sealed class WorldRepository
    {
        readonly string directory;
        public WorldRepository(string directory) { this.directory=Path.GetFullPath(directory); }
        string Resolve(string name)
        {
            if(string.IsNullOrWhiteSpace(name)||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||name.Contains("..")||name.Contains("/")||name.Contains("\\"))throw new ArgumentException("Use a plain save name");
            return Path.Combine(directory,name+".json");
        }
        public void Save(string name,WorldDocument world)
        {
            WorldValidator.Validate(world);
            var path=Resolve(name);
            Directory.CreateDirectory(directory);
            var temp=path+".tmp";
            try
            {
                File.WriteAllText(temp,JsonUtility.ToJson(world,true));
                if(File.Exists(path)) File.Replace(temp,path,path+".bak");
                else File.Move(temp,path);
            }
            catch
            {
                // Clean up orphaned .tmp so it never masquerades as a valid save.
                try { if(File.Exists(temp)) File.Delete(temp); } catch { /* best-effort */ }
                throw;
            }
        }
        public WorldDocument Load(string name)
        {
            var path=Resolve(name);
            if(File.Exists(path))
            {
                try
                {
                    var text=File.ReadAllText(path);
                    var w=JsonUtility.FromJson<WorldDocument>(text);
                    if(w==null) throw new InvalidDataException("Parsed world document is null");
                    WorldValidator.Validate(w);
                    return w;
                }
                catch(Exception primaryEx)
                {
                    var bak=path+".bak";
                    if(File.Exists(bak))
                    {
                        try
                        {
                            var bakText=File.ReadAllText(bak);
                            var wBak=JsonUtility.FromJson<WorldDocument>(bakText);
                            if(wBak==null) throw new InvalidDataException("Parsed backup world document is null");
                            WorldValidator.Validate(wBak);
                            return wBak;
                        }
                        catch
                        {
                            throw primaryEx;
                        }
                    }
                    throw;
                }
            }
            var bakPath=path+".bak";
            if(File.Exists(bakPath))
            {
                var w=JsonUtility.FromJson<WorldDocument>(File.ReadAllText(bakPath));
                if(w==null) throw new InvalidDataException("Parsed backup world document is null");
                WorldValidator.Validate(w);
                return w;
            }
            throw new FileNotFoundException("World not found",path);
        }
    }
    public static class WorldValidator
    {
        static void Require(bool value,string message) { if(!value)throw new InvalidDataException(message); }
        static bool Finite(double x) { return !double.IsNaN(x)&&!double.IsInfinity(x); }
        public static void Validate(WorldDocument w)
        {
            Require(w!=null&&w.schemaVersion==1,"Unsupported world version");
            Require(!string.IsNullOrWhiteSpace(w.id)&&w.chunkSizeM==256,"Invalid world header");
            Require(w.nodes!=null&&w.segments!=null&&w.lanes!=null&&w.objects!=null&&w.districts!=null,"Missing arrays");
            var nodes=new HashSet<string>();var segments=new Dictionary<string,RoadSegment>();var lanes=new Dictionary<string,Lane>();
            // Track which nodes are referenced by at least one segment (to detect isolated nodes).
            var referencedNodes=new HashSet<string>();
            foreach(var n in w.nodes) { Require(n!=null&&!string.IsNullOrWhiteSpace(n.id)&&nodes.Add(n.id),"Duplicate/empty node");Require(Finite(n.x)&&Finite(n.y)&&Finite(n.z),"Invalid node position"); }
            foreach(var s in w.segments) { Require(s!=null&&!string.IsNullOrWhiteSpace(s.id)&&!segments.ContainsKey(s.id),"Duplicate segment");Require(nodes.Contains(s.fromNode)&&nodes.Contains(s.toNode)&&s.fromNode!=s.toNode,"Broken segment");Require(Finite(s.widthM)&&s.widthM>0&&s.laneCount>0&&Finite(s.speedLimitKph)&&s.speedLimitKph>0,"Invalid dimensions");segments.Add(s.id,s);referencedNodes.Add(s.fromNode);referencedNodes.Add(s.toNode); }
            // Verify no isolated nodes (every node must belong to at least one segment).
            foreach(var n in w.nodes) Require(referencedNodes.Contains(n.id),"Isolated node: "+n.id);
            foreach(var l in w.lanes) { Require(l!=null&&!string.IsNullOrWhiteSpace(l.id)&&!lanes.ContainsKey(l.id)&&segments.ContainsKey(l.segmentId),"Invalid lane");var s=segments[l.segmentId];Require((l.fromNode==s.fromNode&&l.toNode==s.toNode)||(l.fromNode==s.toNode&&l.toNode==s.fromNode),"Lane ends outside segment");Require(Finite(l.widthM)&&l.widthM>0&&l.index>=0&&l.index<s.laneCount&&l.successors!=null,"Invalid lane width/index");lanes.Add(l.id,l); }
            foreach(var l in w.lanes)foreach(var next in l.successors)Require(next!=null&&lanes.ContainsKey(next)&&l.toNode==lanes[next].fromNode,"Disconnected successor");
            var ids=new HashSet<string>();foreach(var o in w.objects)Require(o!=null&&!string.IsNullOrWhiteSpace(o.id)&&ids.Add(o.id)&&!string.IsNullOrWhiteSpace(o.catalogId)&&Finite(o.x)&&Finite(o.y)&&Finite(o.z)&&Finite(o.yawDeg),"Invalid object");
            // Validate districts: each must have a non-empty id, finite coordinates, and positive size with Min <= Max bounds.
            var districtIds=new HashSet<string>();
            foreach(var d in w.districts)
            {
                Require(d!=null&&!string.IsNullOrWhiteSpace(d.id)&&districtIds.Add(d.id),"Duplicate/empty district id");
                var maxX=d.minX+d.sizeM;
                var maxZ=d.minZ+d.sizeM;
                Require(Finite(d.minX)&&Finite(d.minZ)&&Finite(maxX)&&Finite(maxZ)&&d.minX<=maxX&&d.minZ<=maxZ,"Invalid district bounds: "+d?.id);
                Require(d.sizeM>0f&&!float.IsNaN(d.sizeM)&&!float.IsInfinity(d.sizeM),"Non-positive district size: "+d?.id);
            }
        }
    }
}
