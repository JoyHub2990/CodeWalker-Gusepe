using CodeWalker.GameFiles;
using CodeWalker.World;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodeWalker.Project.Panels
{
    public sealed class RageQuitZonePanel : UserControl
    {
        private readonly ProjectForm project;
        private WorldForm World => project.WorldForm;
        private readonly List<Vector2> points = new List<Vector2>();
        private readonly List<float> pointHeights = new List<float>();
        private readonly HashSet<YmapEntityDef> manual = new HashSet<YmapEntityDef>();
        private readonly HashSet<YmapEntityDef> excluded = new HashSet<YmapEntityDef>();
        private readonly List<YmapEntityDef> entities = new List<YmapEntityDef>();
        private readonly List<YmapFile> maps = new List<YmapFile>();
        private readonly List<YbnFile> bounds = new List<YbnFile>();
        private readonly List<string> warnings = new List<string>();
        private readonly TextBox nameBox = new TextBox { Text = "building_01", Width = 220 };
        private readonly NumericUpDown minZ = Number(-100), maxZ = Number(300), planeZ = Number(0);
        private readonly NumericUpDown contourPrecision = new NumericUpDown { Minimum = .02m, Maximum = 5, DecimalPlaces = 2, Increment = .05m, Value = .1m, Width = 110 };
        private readonly NumericUpDown pointX = Number(0), pointY = Number(0), pointZ = Number(0);
        private readonly CheckBox surfaceClicks = new CheckBox { Text = "Place clicks on mesh surfaces", Checked = true, AutoSize = true };
        private readonly Label pointLabel = new Label { AutoSize = true, Text = "No zone point selected" };
        private bool updatingPoint;
        private Vector3[] dragStart;
        private readonly Stack<Vector3[]> pointUndo = new Stack<Vector3[]>();
        private readonly Label status = new Label { AutoSize = true, MaximumSize = new System.Drawing.Size(340, 0) };
        private readonly Label pivotLabel = new Label { AutoSize = true, Text = "Pivot: select a building, then Use selection as pivot." };
        private readonly ListBox items = new ListBox { Width = 340, Height = 220, SelectionMode = SelectionMode.MultiExtended };
        private readonly FlowLayoutPanel layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        private CancellationTokenSource cancellation;
        private Vector3 pivot;
        private bool hasPivot, scanned;
        public bool Drawing { get; private set; }
        public bool EditingPoints { get; private set; }
        public bool Interacting => Drawing || EditingPoints;
        public bool SurfaceClicks => surfaceClicks.Checked;
        public int SelectedPoint { get; private set; } = -1;
        public sealed class VisualState
        {
            public Vector3[] Points = new Vector3[0], Fill = new Vector3[0];
            public float MinZ, MaxZ;
            public int Selected = -1;
            public bool Valid;
        }
        public VisualState Visual { get; private set; } = new VisualState();
        public Vector3[] Outline { get; private set; } = new Vector3[0];
        public float OutlineMinZ { get; private set; }
        public float OutlineMaxZ { get; private set; }
        public float PlaneZ => (float)planeZ.Value;

        private static NumericUpDown Number(decimal value) => new NumericUpDown { Minimum = -10000, Maximum = 10000, DecimalPlaces = 2, Increment = 1, Value = value, Width = 110 };
        public RageQuitZonePanel(ProjectForm project)
        {
            this.project = project; Dock = DockStyle.Fill; Controls.Add(layout);
            AddLabel("ZONE PACKAGE — experimental v1.1.7\nVerified LOD loading. Separate standard / hi@ / ma@ YBNs.\nGenerated files: RAGE binary + XML. See REPORT.txt and LODS.txt.");
            layout.Controls.Add(nameBox);
            Button("Use selection as pivot", SetPivot);
            pivotLabel.MaximumSize = new System.Drawing.Size(340, 0); layout.Controls.Add(pivotLabel);
            Row("Drawing plane Z", planeZ); Row("Minimum Z", minZ); Row("Maximum Z", maxZ);
            minZ.ValueChanged += (_, __) => ZoneChanged(); maxZ.ValueChanged += (_, __) => ZoneChanged();
            planeZ.ValueChanged += (_, __) => { for (int i = 0; i < pointHeights.Count; i++) pointHeights[i] = PlaneZ; ZoneChanged(); };
            Row("Contour precision (m)", contourPrecision);
            Button("Pre-draw from viewport selection", async () => { await Run(PreDraw); if (points.Count >= 3) BeginPointEditing(); });
            layout.Controls.Add(surfaceClicks);
            Button("Draw / continue polygon", () => { Drawing = true; EditingPoints = false; World.ActivateZoneSelection(); Status("Click: nearest mesh surface. Alt+click: drawing plane. Enter/right click: finish. Escape: exit."); });
            Button("Finish polygon", FinishDrawing);
            Button("Edit zone points", BeginPointEditing);
            Button("Exit zone editing / select objects", StopDrawing);
            layout.Controls.Add(pointLabel); Row("Point X", pointX); Row("Point Y", pointY); Row("Handle Z", pointZ);
            AddLabel("Drag blue points in XY. Shift+drag snaps to surfaces.\nHandle Z changes the handle height; zone height uses min/max Z.\nCtrl+click an edge to insert a point; Delete removes the selected point.");
            foreach (var field in new[] { pointX, pointY, pointZ }) field.ValueChanged += (_, __) =>
            { if (!updatingPoint && SelectedPoint >= 0) { SaveUndo(); MovePoint(SelectedPoint, new Vector3((float)pointX.Value, (float)pointY.Value, (float)pointZ.Value)); } };
            Button("Delete selected point", DeletePoint);
            Button("Undo zone point edit", UndoPoint);
            Button("Clear zone", () => { StopDrawing(); SaveUndo(); points.Clear(); pointHeights.Clear(); SelectedPoint = -1; entities.Clear(); manual.Clear(); excluded.Clear(); maps.Clear(); bounds.Clear(); ZoneChanged(); RefreshItems(); });
            Button("Scan zone", async () => await Run(Scan));
            Button("Add viewport selection", () => { foreach (var e in Selection()) { manual.Add(e); excluded.Remove(e); if (!entities.Contains(e)) entities.Add(e); } RefreshItems(); });
            Button("Exclude highlighted entries", () => { foreach (YmapEntityDef e in items.SelectedItems.Cast<YmapEntityDef>().ToArray()) { excluded.Add(e); manual.Remove(e); entities.Remove(e); } RefreshItems(); });
            layout.Controls.Add(items);
            Button("Export new package…", async () => await Run(Export));
            Button("Cancel current operation", () => cancellation?.Cancel());
            layout.Controls.Add(status);
            Status("Choose a pivot, draw a polygon, scan, then export. Navigation is available when drawing is stopped.");
        }
        private void AddLabel(string text) => layout.Controls.Add(new Label { AutoSize = true, Text = text, MaximumSize = new System.Drawing.Size(340, 0) });
        private void Row(string text, Control control)
        { var row = new FlowLayoutPanel { Width = 340, Height = 32 }; row.Controls.Add(new Label { Text = text, Width = 150, AutoSize = false }); row.Controls.Add(control); layout.Controls.Add(row); }
        private void Button(string text, Action action)
        { var b = new Button { Text = text, Width = 340, Height = 28 }; b.Click += (_, __) => { if (cancellation == null || text.StartsWith("Cancel")) { try { action(); } catch (Exception ex) { Status(ex.Message); } } }; layout.Controls.Add(b); }
        private void Status(string text) { if (!IsDisposed) status.Text = text; }
        private IEnumerable<YmapEntityDef> Selection()
        {
            var s = World.CurrentMapSelection;
            return s.MultipleSelectionItems?.Length > 0 ? s.MultipleSelectionItems.Where(i => i.EntityDef != null).Select(i => i.EntityDef)
                : s.EntityDef != null ? new[] { s.EntityDef } : Enumerable.Empty<YmapEntityDef>();
        }
        private void SetPivot()
        {
            var selected = Selection().ToArray();
            if (selected.Length != 1) throw new InvalidOperationException("Select exactly one building in the viewport.");
            pivot = selected[0].Position; hasPivot = true;
            nameBox.Text = new string(selected[0].Name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '_').Take(48).ToArray());
            pivotLabel.Text = "Pivot: " + selected[0].Name + " — " + pivot;
            planeZ.Value = Clamp(pivot.Z); minZ.Value = Clamp(pivot.Z - 10); maxZ.Value = Clamp(pivot.Z + 150);
            manual.Add(selected[0]); ZoneChanged();
        }
        private static decimal Clamp(float value) => Math.Max(-10000, Math.Min(10000, (decimal)value));
        public void AddPoint(Vector3 world)
        {
            if (!Drawing || cancellation != null) return;
            if (points.Count >= 256) { Status("Maximum 256 points."); return; }
            SaveUndo(); points.Add(new Vector2(world.X, world.Y)); pointHeights.Add(world.Z); SelectedPoint = points.Count - 1;
            ZoneChanged(); Status(points.Count + " points. Enter to finish; the blue handle is at the clicked surface.");
        }
        private void SaveUndo() => pointUndo.Push(Outline.ToArray());
        public void UndoPoint()
        {
            if (pointUndo.Count == 0) return;
            SetPoints(pointUndo.Pop()); Status("Zone edit undone.");
        }
        private void SetPoints(Vector3[] values)
        { points.Clear(); pointHeights.Clear(); points.AddRange(values.Select(p => new Vector2(p.X, p.Y))); pointHeights.AddRange(values.Select(p => p.Z)); SelectedPoint = -1; ZoneChanged(); }
        public void BeginPointEditing()
        { if (cancellation != null) return; Drawing = false; EditingPoints = true; World.ActivateZoneSelection(); Status("ZONE POINTS mode: click/drag blue handles. Escape returns to object selection."); }
        public void EndInteraction()
        { EndDrag(); Drawing = false; EditingPoints = false; }
        public void StopDrawing()
        { EndInteraction(); World.RestoreZoneSelection(); Status("Zone editing stopped. Object selection restored."); }
        public void FinishDrawing() { ValidateZone(); BeginPointEditing(); }
        public void SelectPoint(int index)
        { SelectedPoint = index >= 0 && index < points.Count ? index : -1; ZoneChanged(); }
        public void BeginDrag() { dragStart = Outline.ToArray(); }
        public void EndDrag(bool cancel = false)
        {
            if (dragStart == null) return;
            var old = dragStart; dragStart = null;
            if (cancel) SetPoints(old); else if (!old.SequenceEqual(Outline)) pointUndo.Push(old);
        }
        public void MovePoint(int index, Vector3 world)
        {
            if (index < 0 || index >= points.Count || cancellation != null) return;
            points[index] = new Vector2(world.X, world.Y); pointHeights[index] = world.Z; SelectedPoint = index; ZoneChanged();
            Status(Visual.Valid ? "Point " + (index + 1) + " moved. Zone updated." : "Invalid/open polygon (red): fix crossings or duplicate points before exporting.");
        }
        public void InsertPoint(int after, Vector3 world)
        {
            if (points.Count >= 256) return;
            SaveUndo(); points.Insert(after + 1, new Vector2(world.X, world.Y)); pointHeights.Insert(after + 1, world.Z); SelectedPoint = after + 1; ZoneChanged();
        }
        public void DeletePoint()
        {
            if (SelectedPoint < 0 || SelectedPoint >= points.Count) return;
            SaveUndo(); points.RemoveAt(SelectedPoint); pointHeights.RemoveAt(SelectedPoint); SelectedPoint = -1; ZoneChanged();
        }
        public void NoSurface() => Status("No mesh surface under this click. Alt+click explicitly uses the drawing plane.");
        private void ZoneChanged()
        {
            scanned = false; OutlineMinZ = (float)minZ.Value; OutlineMaxZ = (float)maxZ.Value;
            Outline = points.Select((p, i) => new Vector3(p.X, p.Y, i < pointHeights.Count ? pointHeights[i] : PlaneZ)).ToArray();
            var state = new VisualState { Points = Outline, MinZ = OutlineMinZ, MaxZ = OutlineMaxZ, Selected = SelectedPoint };
            try
            {
                var origin = points.Count > 0 ? points[0] : Vector2.Zero;
                var polygon = new ZoneClipper(points.Select(p => p - origin), 0, 1);
                state.Fill = polygon.GetTriangles().SelectMany(t => t.Select(p => new Vector3(p + origin, OutlineMinZ))).ToArray();
                state.Valid = OutlineMaxZ > OutlineMinZ;
            }
            catch (ArgumentException) { }
            Visual = state; // One immutable snapshot for the rendering thread.
            updatingPoint = true;
            if (SelectedPoint >= 0 && SelectedPoint < Outline.Length)
            {
                var p = Outline[SelectedPoint]; pointX.Value = Clamp(p.X); pointY.Value = Clamp(p.Y); pointZ.Value = Clamp(p.Z);
                pointLabel.Text = "Selected zone point: " + (SelectedPoint + 1);
            }
            else pointLabel.Text = "No zone point selected";
            updatingPoint = false;
        }
        private void ValidateZone() => new ZoneClipper(points.Select(p => p - new Vector2(pivot.X, pivot.Y)), (float)minZ.Value - pivot.Z, (float)maxZ.Value - pivot.Z);
        private void RefreshItems()
        { items.BeginUpdate(); items.Items.Clear(); items.Items.AddRange(entities.Cast<object>().ToArray()); items.EndUpdate(); Status(entities.Count + " candidate entities. Manual additions are also clipped at export."); }
        private async Task Run(Func<CancellationToken, Task> action)
        {
            if (cancellation != null) return;
            StopDrawing(); cancellation = new CancellationTokenSource();
            foreach (Control control in layout.Controls) control.Enabled = control is Button button && button.Text.StartsWith("Cancel");
            try { await action(cancellation.Token); }
            catch (OperationCanceledException) { Status("Cancelled. Partial output, if any, is marked FAILED.txt."); }
            catch (Exception ex) { Status(ex.Message); MessageBox.Show(this, ex.Message, "Zone package", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            finally { cancellation.Dispose(); cancellation = null; if (!IsDisposed) foreach (Control control in layout.Controls) control.Enabled = true; }
        }
        private async Task PreDraw(CancellationToken token)
        {
            var selected = Selection().Distinct().ToArray();
            if (selected.Length == 0) throw new InvalidOperationException("Select one or more static objects in the viewport first.");
            var sources = new List<Tuple<DrawableBase, Matrix>>();
            foreach (var entity in selected)
            {
                var watch = Stopwatch.StartNew(); DrawableBase draw;
                do
                {
                    token.ThrowIfCancellationRequested();
                    draw = entity.Archetype != null ? World.GameFileCache.TryGetDrawable(entity.Archetype) : null;
                    if (draw != null) break;
                    if (watch.Elapsed.TotalSeconds > 10) throw new InvalidOperationException("Mesh unavailable: " + entity.Name);
                    Status("Loading contour mesh: " + entity.Name); await Task.Delay(100, token);
                } while (true);
                if (entity.IsMlo || !(draw is Drawable)) throw new NotSupportedException("Pre-draw requires static YDR/YDD objects: " + entity.Name);
                sources.Add(Tuple.Create(draw, Matrix.Scaling(entity.Scale) * Matrix.RotationQuaternion(entity.Orientation) * Matrix.Translation(entity.Position)));
            }
            float precision = (float)contourPrecision.Value;
            Status("Projecting selected mesh triangles and tracing their contour…");
            var vertices = await Task.Run(() => sources.SelectMany(s => ZoneGeometry.Triangles(s.Item1, s.Item2, token)).ToArray(), token);
            var footprint = await Task.Run(() => ZoneGeometry.TraceFootprint(vertices, precision, token), token);
            SaveUndo();
            var lo = vertices.Aggregate(Vector3.Min); var hi = vertices.Aggregate(Vector3.Max);
            minZ.Value = Clamp(lo.Z - .05f); maxZ.Value = Clamp(hi.Z + .05f); planeZ.Value = Clamp(hi.Z);
            SetPoints(footprint.Points.Select(p => new Vector3(p, hi.Z)).ToArray());
            foreach (var e in selected) manual.Add(e);
            string note = footprint.Points.Length + " points; mesh contour grid " + footprint.CellSize.ToString("0.###") + " m, simplification " + footprint.Simplification.ToString("0.###") + " m.";
            if (footprint.BridgedIslands) note += " Separate/ambiguous contours enclosed by a convex hull; review the points.";
            if (footprint.IgnoredHoles > 0) note += " Interior holes filled (zone supports one outer contour).";
            Status(note);
            MessageBox.Show(this, note, "Pre-draw complete");
        }
        private async Task Scan(CancellationToken token)
        {
            scanned = false;
            ValidateZone();
            if (!World.GameFileCache.IsInited || World.Space.MapDataStore == null || World.Space.BoundsStore == null) throw new InvalidOperationException("Wait for the world cache to finish loading.");
            var min = new Vector3(points.Min(p => p.X), points.Min(p => p.Y), (float)minZ.Value);
            var max = new Vector3(points.Max(p => p.X), points.Max(p => p.Y), (float)maxZ.Value);
            var volume = new ZoneClipper(points, min.Z, max.Z);
            uint[] mapHashes, boundHashes;
            lock (World.Renderer.RenderSyncRoot)
            {
                mapHashes = World.Space.MapDataStore.GetItems(ref min, ref max).Select(n => n.Name.Hash).Distinct().Where(World.Space.IsYmapCurrentlyAvailable).ToArray();
                boundHashes = World.Space.BoundsStore.GetItems(ref min, ref max).Select(n => n.Name.Hash).Distinct().ToArray();
            }
            var cache = World.GameFileCache;
            // Apply project overrides before loading: an edited file need not wait for its game counterpart.
            var projectMaps = (project.CurrentProjectFile?.YmapFiles ?? new List<YmapFile>()).ToArray();
            var projectBounds = (project.CurrentProjectFile?.YbnFiles ?? new List<YbnFile>()).ToArray();
            var projectMapHashes = new HashSet<uint>(projectMaps.Select(m => m._CMapData.name.Hash));
            var projectBoundNames = new HashSet<string>(projectBounds.Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
            string MapName(uint hash) => cache.GetYmapEntry(hash)?.Path ?? ("YMAP 0x" + hash.ToString("X8"));
            string BoundName(uint hash) => cache.GetYbnEntry(hash)?.Path ?? ("YBN 0x" + hash.ToString("X8"));
            var loadedMaps = await ZoneResourceLoader.LoadAsync(mapHashes.Where(h => !projectMapHashes.Contains(h)), cache.GetYmap, MapName,
                (done, total, file) => Status("Scan YMAP: " + done + "/" + total + "\n" + file), token);
            var loadedBounds = await ZoneResourceLoader.LoadAsync(boundHashes.Where(h => !projectBoundNames.Contains(cache.GetYbnEntry(h)?.Name)), cache.GetYbn, BoundName,
                (done, total, file) => Status("Scan YBN: " + done + "/" + total + "\n" + file), token);
            var scanWarnings = new List<string>();
            scanWarnings.AddRange(loadedMaps.Missing.Select(h => "Indexed map not found: " + MapName(h)));
            scanWarnings.AddRange(loadedBounds.Missing.Select(h => "Indexed collision not found: " + BoundName(h)));
            var mapFiles = loadedMaps.Files.Values.Concat(projectMaps).Distinct().ToList();
            var boundFiles = loadedBounds.Files.Values.Concat(projectBounds).Distinct().ToList();
            var scanEntities = new List<YmapEntityDef>();
            foreach (var map in mapFiles)
            {
                token.ThrowIfCancellationRequested();
                lock (World.Renderer.RenderSyncRoot) map.InitYmapEntityArchetypes(cache);
                foreach (var entity in map.AllEntities ?? new YmapEntityDef[0])
                {
                    if (entity.Archetype == null) { scanWarnings.Add(map.Name + ": missing archetype " + entity.Name); continue; }
                    var a = entity.BBMin; var b = entity.BBMax;
                    if (b.X < min.X || a.X > max.X || b.Y < min.Y || a.Y > max.Y || b.Z < min.Z || a.Z > max.Z) continue;
                    if (ZonePackageExporter.IsHd(entity) && !volume.Contains(entity.Position)) continue;
                    if (!excluded.Contains(entity)) scanEntities.Add(entity);
                }
            }
            foreach (var entity in manual) if (!scanEntities.Contains(entity) && !excluded.Contains(entity)) scanEntities.Add(entity);
            // Commit only a complete scan. Cancellation/failure keeps the previous candidate list and the drawn zone.
            token.ThrowIfCancellationRequested();
            maps.Clear(); maps.AddRange(mapFiles); bounds.Clear(); bounds.AddRange(boundFiles);
            entities.Clear(); entities.AddRange(scanEntities); warnings.Clear(); warnings.AddRange(scanWarnings);
            // HD is selected by origin (plus manual additions). LOD candidates are clipped at export.
            scanned = true; RefreshItems();
            Status(entities.Count + " candidates, " + maps.Count + " maps, " + bounds.Count + " collision files. Review candidates and exclude unwanted variants before export.");
        }
        private async Task Export(CancellationToken token)
        {
            if (!hasPivot) throw new InvalidOperationException("Choose the pivot building first.");
            ValidateZone();
            if (!scanned) await Scan(token);
            string folder;
            using (var dialog = new FolderBrowserDialog { Description = "Choose the parent folder. A new folder with the package name will be created." })
            { if (dialog.ShowDialog(this) != DialogResult.OK) return; folder = dialog.SelectedPath; }
            var request = new ZonePackageExporter.Request { Name = nameBox.Text.Trim(), OutputDirectory = folder, Pivot = pivot,
                Polygon = points.ToArray(), MinZ = (float)minZ.Value, MaxZ = (float)maxZ.Value, Maps = maps.ToList(), Collisions = bounds.ToList(), Warnings = warnings.ToList() };
            string importer = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "import_zone_package.py");
            if (File.Exists(importer)) request.ImporterPath = importer;
            var cache = World.GameFileCache;
            foreach (var entity in entities.Distinct())
            {
                token.ThrowIfCancellationRequested();
                if (entity.Archetype == null) entity.SetArchetype(cache.GetArchetype(entity._CEntityDef.archetypeName));
                if (entity.Archetype == null) { request.Warnings.Add(entity.Name + ": missing archetype; omitted."); continue; }
                var drawable = await ZoneResourceLoader.LoadDrawableAsync(entity.Archetype, cache.GetYdd, cache.GetYdr, cache.GetYft,
                    file => Status("Loading drawable " + entity.Name + "…\n" + file), token);
                if (ZonePackageExporter.IsHd(entity))
                {
                    request.Assets.Add(new ZonePackageExporter.Asset { Entity = entity, Drawable = drawable as Drawable, FragmentDrawable = drawable as FragDrawable });
                    if (drawable == null) request.Warnings.Add(entity.Name + ": original HD reference retained; drawable did not load, so embedded collisions could not be extracted.");
                    continue; // No HD texture loading or mesh serialization is needed.
                }
                if (drawable == null) throw new InvalidOperationException(entity.Name + ": LOD resource not found. Export stopped to avoid an incomplete LOD level.");
                if (!(drawable is Drawable d)) { request.Warnings.Add(entity.Name + ": unsupported LOD drawable type " + drawable.GetType().Name + "; omitted."); continue; }
                var asset = new ZonePackageExporter.Asset { Entity = entity, Drawable = d };
                var textureHashes = new List<uint>(); var seen = new HashSet<uint>(); uint txdHash = entity.Archetype.TextureDict.Hash;
                while (txdHash != 0 && seen.Add(txdHash))
                { textureHashes.Add(txdHash); txdHash = cache.TryGetParentYtdHash(txdHash); }
                var texturesLoaded = await ZoneResourceLoader.LoadAsync(textureHashes, cache.GetYtd,
                    h => cache.GetYtdEntry(h)?.Path ?? ("YTD 0x" + h.ToString("X8")),
                    (done, total, file) => Status("Textures for " + entity.Name + ": " + done + "/" + total + "\n" + file), token);
                var txds = textureHashes.Where(texturesLoaded.Files.ContainsKey).Select(h => texturesLoaded.Files[h]).ToList();
                foreach (uint missing in texturesLoaded.Missing) request.Warnings.Add(entity.Name + ": texture dictionary not found: 0x" + missing.ToString("X8"));
                foreach (var texture in d.ShaderGroup?.Shaders?.data_items?.SelectMany(s => s.ParametersList?.Parameters ?? new ShaderParameter[0]).Where(p => p.DataType == 0).Select(p => p.Data as TextureBase).Where(t => t != null) ?? Enumerable.Empty<TextureBase>())
                {
                    uint hash = JenkHash.GenHash(texture.Name.ToLowerInvariant());
                    var resolved = d.ShaderGroup?.TextureDictionary?.Lookup(hash) ?? txds.Where(t => t.Loaded).Select(t => t.TextureDict?.Lookup(hash)).FirstOrDefault(t => t != null);
                    if (resolved == null)
                    {
                        var fallback = await ZoneResourceLoader.LoadAsync(new[] { hash }, cache.TryGetTextureDictForTexture,
                            h => "texture lookup: " + texture.Name + " (0x" + h.ToString("X8") + ")",
                            (done, total, file) => Status("Resolving " + file), token);
                        if (fallback.Files.TryGetValue(hash, out var txd)) resolved = txd.TextureDict?.Lookup(hash);
                    }
                    if (resolved == null) request.Warnings.Add(entity.Name + ": texture not resolved: " + texture.Name);
                    else asset.Textures.Add(resolved);
                }
                request.Assets.Add(asset);
            }
            var exporter = new ZonePackageExporter(request, token);
            var progress = new Progress<string>(Status);
            var result = await Task.Run(() => exporter.Export(s => ((IProgress<string>)progress).Report(s)), token);
            Status("Exported " + result.References + " HD references, " + result.Assets + " LOD drawables. " + result.Warnings.Count + " warning(s). " + result.Directory);
            MessageBox.Show(this, "Package: " + result.Directory + "\n\n" + result.References + " original HD references\n" + result.Assets + " generated LOD drawables\n" + result.CollisionTriangles + " collision triangles\n" + result.CollisionPrimitives + " native collision primitives\n" + result.CutCollisionPrimitives + " primitives clipped\n" + result.PropCollisionParts + " prop collision parts processed\n" + result.OcclusionTriangles + " occlusion triangles\n\nRAGE binary + XML for every generated resource.\n" + result.Warnings.Count + " warning(s): read REPORT.txt before use."
                + (result.FinalizationNotice == null ? "" : "\n\n" + result.FinalizationNotice), "Zone package exported");
        }
        protected override void Dispose(bool disposing) { if (disposing) cancellation?.Cancel(); base.Dispose(disposing); }
    }
}
