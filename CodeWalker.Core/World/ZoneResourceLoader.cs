using CodeWalker.GameFiles;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeWalker.World
{
    /// <summary>Cooperates with the bounded, evicting viewport cache without changing its global scheduling.</summary>
    public static class ZoneResourceLoader
    {
        // Distinguish absent resources from files still queued/loading. A null
        // TryGetDrawable result conflates these states and used to omit big SLODs
        // after ten seconds, leaving only their small emissive proxy in the export.
        public static async Task<DrawableBase> LoadDrawableAsync(Archetype archetype,
            Func<uint, YddFile> getYdd, Func<uint, YdrFile> getYdr, Func<uint, YftFile> getYft,
            Action<string> progress, CancellationToken token, int timeoutMilliseconds = 45000, int pollMilliseconds = 100)
        {
            if (archetype == null) return null;
            uint hash = archetype.Hash;
            async Task<T> Load<T>(uint key, Func<uint, T> request, string name) where T : GameFile
            {
                var loaded = await LoadAsync(new[] { key }, request, h => name,
                    (done, total, pendingName) => progress?.Invoke(pendingName), token,
                    timeoutMilliseconds: timeoutMilliseconds, pollMilliseconds: pollMilliseconds);
                return loaded.Files.TryGetValue(key, out var file) ? file : null;
            }
            if (archetype.DrawableDict != 0)
            {
                var ydd = await Load(archetype.DrawableDict, getYdd, archetype.DrawableDict + ".ydd / " + archetype.Name);
                if (ydd != null)
                {
                    if (ydd.Dict != null && ydd.Dict.TryGetValue(hash, out var draw) && draw != null) return draw;
                    throw new InvalidDataException("Loaded drawable dictionary has no entry for " + archetype.Name
                        + ": " + (ydd.RpfFileEntry?.Path ?? archetype.DrawableDict + ".ydd"));
                }
            }
            var ydr = await Load(hash, getYdr, archetype.Name + ".ydr");
            if (ydr != null)
                return ydr.Drawable ?? throw new InvalidDataException("Loaded YDR has no drawable: " + archetype.Name);
            var yft = await Load(hash, getYft, archetype.Name + ".yft");
            if (yft != null)
                return yft.Fragment?.Drawable ?? throw new InvalidDataException("Loaded YFT has no drawable: " + archetype.Name);
            return null;
        }

        public sealed class Result<T> where T : GameFile
        {
            // Keep strong references to completed files for the whole scan/export.
            public Dictionary<uint, T> Files { get; } = new Dictionary<uint, T>();
            public List<uint> Missing { get; } = new List<uint>();
        }
        private sealed class Pending<T> where T : GameFile
        {
            public uint Hash;
            public T File;
            public Stopwatch Watch = Stopwatch.StartNew();
        }

        public static async Task<Result<T>> LoadAsync<T>(IEnumerable<uint> hashes, Func<uint, T> request,
            Func<uint, string> describe, Action<int, int, string> progress, CancellationToken token,
            int concurrency = 4, int timeoutMilliseconds = 45000, int pollMilliseconds = 100) where T : GameFile
        {
            if (concurrency < 1 || concurrency > 4 || timeoutMilliseconds < 1 || pollMilliseconds < 1)
                throw new ArgumentOutOfRangeException("Use 1–4 pending requests and positive timing values.");
            var queue = new Queue<uint>(hashes.Distinct());
            int total = queue.Count;
            var pending = new List<Pending<T>>();
            var result = new Result<T>();
            while (queue.Count > 0 || pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                while (queue.Count > 0 && pending.Count < concurrency) pending.Add(new Pending<T> { Hash = queue.Dequeue() });
                foreach (var item in pending.ToArray())
                {
                    token.ThrowIfCancellationRequested();
                    // GetYmap/GetYbn/GetYtd must be called again: a full queue silently rejects requests,
                    // and eviction may replace an unloaded object. Watching the original reference alone can never finish.
                    if (item.File == null || !item.File.Loaded) item.File = request(item.Hash);
                    if (item.File == null)
                    { result.Missing.Add(item.Hash); pending.Remove(item); }
                    else if (item.File.Loaded)
                    { result.Files.Add(item.Hash, item.File); pending.Remove(item); }
                    else if (item.Watch.ElapsedMilliseconds >= timeoutMilliseconds)
                    {
                        throw new TimeoutException("Zone resource did not load after repeated requests: " + describe(item.Hash)
                            + "\nCompleted: " + result.Files.Count + "/" + total
                            + ". The scan/export has stopped; this file has not been silently omitted."
                            + "\nCheck the CodeWalker error log or cache capacity, then retry.");
                    }
                }
                progress?.Invoke(result.Files.Count + result.Missing.Count, total, pending.Count > 0 ? describe(pending[0].Hash) : "");
                if (pending.Count > 0) await Task.Delay(pollMilliseconds, token);
            }
            return result;
        }
    }
}
