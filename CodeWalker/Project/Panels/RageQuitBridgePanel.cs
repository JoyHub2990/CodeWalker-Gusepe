using CodeWalker.GameFiles;
using CodeWalker.World;
using CodeWalker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodeWalker.Project.Panels
{
    public partial class RageQuitBridgePanel : ProjectPanel
    {
        public ProjectForm ProjectForm { get; private set; }

        private static readonly HttpClient _httpClient =
            new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        // ── Persistent lists ──────────────────────────────────────────────────

        // Stores entity instances by object reference — same entity object can't be
        // added twice, but multiple instances sharing the same name are all kept.
        private readonly List<YmapEntityDef> _entityList = new List<YmapEntityDef>();

        // Entities currently hidden in the viewport (toggled by the Hide button).
        private readonly HashSet<YmapEntityDef> _hiddenEntities = new HashSet<YmapEntityDef>();

        private readonly Dictionary<string, string> _ymapList =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // YMAP → YmapFile reference, populated when adding to the list.
        private readonly Dictionary<string, YmapFile> _ymapFileRefs =
            new Dictionary<string, YmapFile>(StringComparer.OrdinalIgnoreCase);

        // YMAP paths currently hidden in the viewport (toggled by the Hide button).
        private readonly HashSet<string> _hiddenYmapPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // YBN list: key = YBN name (case-insensitive, deduped — same file once)
        private readonly HashSet<string> _ybnList =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Last selection captured from the Project Explorer
        private object _lastExplorerSelection;

        // ── Constructor ───────────────────────────────────────────────────────

        public RageQuitBridgePanel(ProjectForm projectForm)
        {
            ProjectForm = projectForm;
            InitializeComponent();
        }

        // ── Explorer selection bridge ─────────────────────────────────────────

        public void OnProjectExplorerItemSelected(object item)
        {
            _lastExplorerSelection = item;
        }

        // ── Viewport selection ────────────────────────────────────────────────

        private List<YmapEntityDef> GetViewportSelection()
        {
            var result = new List<YmapEntityDef>();
            var wf = ProjectForm?.WorldForm;
            if (wf == null) return result;

            var sel = wf.CurrentMapSelection;

            if (sel.MultipleSelectionItems != null && sel.MultipleSelectionItems.Length > 0)
            {
                foreach (var item in sel.MultipleSelectionItems)
                    if (item.EntityDef != null) result.Add(item.EntityDef);
            }
            else if (sel.EntityDef != null)
            {
                result.Add(sel.EntityDef);
            }
            return result;
        }

        // ── YMAP extraction ───────────────────────────────────────────────────

        private List<(string Path, string Filename, YmapFile YmapRef)> GetYmapsFromSelection(object item)
        {
            var result = new List<(string, string, YmapFile)>();
            if (item == null) return result;

            if (item is object[] arr)
            {
                foreach (var obj in arr) result.AddRange(GetYmapsFromSelection(obj));
                return result;
            }
            if (item is YmapFile ymap)
            {
                string path = ymap.FilePath ?? ymap.RpfFileEntry?.Path ?? "";
                string name = ymap.Name ?? System.IO.Path.GetFileName(path);
                if (!string.IsNullOrEmpty(path)) result.Add((path, name, ymap));
            }
            return result;
        }

        // ── ListView refresh ──────────────────────────────────────────────────

        private void RefreshEntityList()
        {
            var ic = CultureInfo.InvariantCulture;
            EntityListView.Items.Clear();
            foreach (var ent in _entityList)
            {
                var pos = ent.Position;
                var ori = ent.Orientation;
                bool hidden = _hiddenEntities.Contains(ent);
                var lvi = new ListViewItem(hidden ? "○ " + ent.Name : "● " + ent.Name);
                lvi.SubItems.Add(string.Format(ic, "{0:F1}, {1:F1}, {2:F1}", pos.X, pos.Y, pos.Z));
                lvi.SubItems.Add(string.Format(ic, "{0:F2}, {1:F2}, {2:F2}, {3:F2}", ori.X, ori.Y, ori.Z, ori.W));
                lvi.Tag = ent;
                if (hidden)
                {
                    lvi.ForeColor = System.Drawing.Color.Gray;
                    lvi.Font = new System.Drawing.Font(EntityListView.Font, System.Drawing.FontStyle.Strikeout);
                }
                EntityListView.Items.Add(lvi);
            }
            EntityCountLabel.Text = $"{_entityList.Count} entity/ies ({_hiddenEntities.Count} hidden)";

            // Button label reflects next action: Show when all hidden, Hide otherwise.
            if (_entityList.Count == 0)
                HideButton.Text = "o Hide";
            else if (_entityList.All(ent => _hiddenEntities.Contains(ent)))
                HideButton.Text = "o Show";
            else
                HideButton.Text = "o Hide";
        }

        private void RefreshYmapList()
        {
            YmapListView.Items.Clear();
            foreach (var kvp in _ymapList)
            {
                bool hidden = _hiddenYmapPaths.Contains(kvp.Key);
                var lvi = new ListViewItem(hidden ? "○ " + kvp.Value : "● " + kvp.Value);
                lvi.SubItems.Add(kvp.Key);
                lvi.Tag = kvp.Key;
                if (hidden)
                {
                    lvi.ForeColor = System.Drawing.Color.Gray;
                    lvi.Font = new System.Drawing.Font(YmapListView.Font, System.Drawing.FontStyle.Strikeout);
                }
                YmapListView.Items.Add(lvi);
            }
            YmapCountLabel.Text = $"{_ymapList.Count} YMAP(s) ({_hiddenYmapPaths.Count} hidden)";

            // Button label reflects next action: Show when all hidden, Hide otherwise.
            if (_ymapList.Count == 0)
                YmapHideButton.Text = "o Hide";
            else if (_ymapList.Keys.All(p => _hiddenYmapPaths.Contains(p)))
                YmapHideButton.Text = "o Show";
            else
                YmapHideButton.Text = "o Hide";
        }

        // ── JSON builders ─────────────────────────────────────────────────────

        private string BuildEntityJson(IEnumerable<YmapEntityDef> entities, bool withPositions, bool importAsAsset)
        {
            var sb = new StringBuilder();
            var ic = CultureInfo.InvariantCulture;
            string assetFlag = importAsAsset ? "true" : "false";

            if (withPositions)
            {
                sb.Append($"{{\"import_as_asset\":{assetFlag},\"entities\":[");
                bool first = true;
                foreach (var ent in entities)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    var pos = ent.Position;
                    var ori = ent.Orientation;
                    sb.Append("{");
                    sb.Append($"\"name\":\"{EscapeJson(ent.Name)}\",");
                    sb.AppendFormat(ic, "\"position\":{{\"x\":{0},\"y\":{1},\"z\":{2}}},", pos.X, pos.Y, pos.Z);
                    sb.AppendFormat(ic, "\"rotation\":{{\"x\":{0},\"y\":{1},\"z\":{2},\"w\":{3}}}", ori.X, ori.Y, ori.Z, ori.W);
                    sb.Append("}");
                }
                sb.Append("]}");
            }
            else
            {
                // Assets-only mode: deduplicate by name so Blender only downloads each file once.
                sb.Append($"{{\"import_as_asset\":{assetFlag},\"assets\":[");
                bool first = true;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var ent in entities)
                {
                    if (!seen.Add(ent.Name)) continue;
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append($"\"{EscapeJson(ent.Name)}\"");
                }
                sb.Append("]}");
            }
            return sb.ToString();
        }

        private string BuildYmapJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"ymaps\":[");
            bool first = true;
            foreach (var kvp in _ymapList)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"{{\"path\":\"{EscapeJson(kvp.Key)}\",\"filename\":\"{EscapeJson(kvp.Value)}\"}}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static string EscapeJson(string s) =>
            s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

        // ── HTTP ──────────────────────────────────────────────────────────────

        private async Task SendJsonAsync(string json)
        {
            int port = (int)PortNumericUpDown.Value;
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"http://localhost:{port}", content);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
        }

        private void SetStatus(string text) => StatusLabel.Text = text;

        // ── Entity handlers ───────────────────────────────────────────────────

        private void AddToListButton_Click(object sender, EventArgs e)
        {
            var entities = GetViewportSelection();
            if (entities.Count == 0) { SetStatus("No entities selected in the viewport."); return; }

            int added = 0;
            foreach (var ent in entities)
            {
                // Deduplicate by object reference — never add the exact same entity twice,
                // but multiple instances with the same name are all kept.
                if (!string.IsNullOrEmpty(ent.Name) && !_entityList.Contains(ent))
                { _entityList.Add(ent); added++; }
            }

            RefreshEntityList();
            SetStatus($"Added {added} entity/ies ({entities.Count - added} already in list).");
        }

        private void RemoveButton_Click(object sender, EventArgs e)
        {
            var toRemove = EntityListView.SelectedItems
                .Cast<ListViewItem>()
                .Select(lvi => lvi.Tag as YmapEntityDef)
                .Where(ent => ent != null)
                .ToList();

            foreach (var ent in toRemove)
            {
                _entityList.Remove(ent);
                _hiddenEntities.Remove(ent);
            }
            RefreshEntityList();
            SetStatus("Removed selected entities.");
        }

        private void ClearListButton_Click(object sender, EventArgs e)
        {
            _entityList.Clear();
            _hiddenEntities.Clear();
            RefreshEntityList();
            SetStatus("Entity list cleared.");
        }

        private void HideButton_Click(object sender, EventArgs e)
        {
            if (_entityList.Count == 0) { SetStatus("List is empty — add entities first."); return; }

            // Toggle entire list: if all are hidden → show all; otherwise → hide all.
            bool allHidden = _entityList.All(ent => _hiddenEntities.Contains(ent));
            if (allHidden)
            {
                foreach (var ent in _entityList) _hiddenEntities.Remove(ent);
                SetStatus($"Showed all {_entityList.Count} entity/ies in viewport.");
            }
            else
            {
                foreach (var ent in _entityList) _hiddenEntities.Add(ent);
                SetStatus($"Hidden all {_entityList.Count} entity/ies from viewport.");
            }

            RefreshEntityList();
        }

        /// <summary>
        /// Called every render frame by WorldForm to re-populate Renderer.HideEntities
        /// (which is cleared at the start of each frame).
        /// </summary>
        public void PopulateHiddenEntities()
        {
            if (_hiddenEntities.Count == 0 && _hiddenYmapPaths.Count == 0) return;
            var renderer = ProjectForm?.WorldForm?.Renderer;
            if (renderer == null) return;

            foreach (var ent in _hiddenEntities)
                renderer.RenderHideEntity(ent);

            foreach (var path in _hiddenYmapPaths)
            {
                if (!_ymapFileRefs.TryGetValue(path, out var ymap)) continue;
                if (ymap.AllEntities == null) continue;
                foreach (var ent in ymap.AllEntities)
                    renderer.RenderHideEntity(ent);
            }
        }

        private async void SendListButton_Click(object sender, EventArgs e)
        {
            if (_entityList.Count == 0) { SetStatus("List is empty — use \"Add to List\" first."); return; }
            SendListButton.Enabled = false;
            SetStatus($"Sending {_entityList.Count} entity/ies...");
            try
            {
                await SendJsonAsync(BuildEntityJson(_entityList,
                    WithPositionsCheckBox.Checked, ImportAsAssetCheckBox.Checked));
                SetStatus($"Sent {_entityList.Count} entity/ies to Blender.");
            }
            catch (TaskCanceledException) { SetStatus("Timeout — is the server running in Blender?"); }
            catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
            finally { SendListButton.Enabled = true; }
        }

        private async void SendSelectionButton_Click(object sender, EventArgs e)
        {
            var entities = GetViewportSelection();
            if (entities.Count == 0) { SetStatus("No entities selected in the viewport."); return; }

            // Without positions: deduplicate by name (same asset downloaded once).
            // With positions: keep all instances — each has its own world transform.
            bool withPos = WithPositionsCheckBox.Checked;
            var toSend = withPos
                ? entities
                : entities.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                          .Select(g => g.First()).ToList();

            SendSelectionButton.Enabled = false;
            SetStatus($"Sending {toSend.Count} entity/ies...");
            try
            {
                await SendJsonAsync(BuildEntityJson(toSend, withPos, ImportAsAssetCheckBox.Checked));
                SetStatus($"Sent {toSend.Count} entity/ies to Blender.");
            }
            catch (TaskCanceledException) { SetStatus("Timeout — is the server running in Blender?"); }
            catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
            finally { SendSelectionButton.Enabled = true; }
        }

        // ── YMAP handlers ─────────────────────────────────────────────────────

        private void YmapAddButton_Click(object sender, EventArgs e)
        {
            var ymaps = GetYmapsFromSelection(_lastExplorerSelection);
            if (ymaps.Count == 0) { SetStatus("No YMAP selected in the Project Explorer."); return; }

            int added = 0;
            foreach (var (path, filename, ymapRef) in ymaps)
            {
                if (!_ymapList.ContainsKey(path))
                {
                    _ymapList[path] = filename;
                    if (ymapRef != null) _ymapFileRefs[path] = ymapRef;
                    added++;
                }
            }

            RefreshYmapList();
            SetStatus($"Added {added} YMAP(s) ({ymaps.Count - added} already in list).");
        }

        private void YmapRemoveButton_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem lvi in YmapListView.SelectedItems)
            {
                if (lvi.Tag is string path)
                {
                    _ymapList.Remove(path);
                    _ymapFileRefs.Remove(path);
                    _hiddenYmapPaths.Remove(path);
                }
            }

            RefreshYmapList();
            SetStatus("Removed selected YMAP(s).");
        }

        private void YmapHideButton_Click(object sender, EventArgs e)
        {
            if (_ymapList.Count == 0) { SetStatus("List is empty — add YMAPs first."); return; }

            // Toggle entire list: if all are hidden → show all; otherwise → hide all.
            bool allHidden = _ymapList.Keys.All(p => _hiddenYmapPaths.Contains(p));
            if (allHidden)
            {
                _hiddenYmapPaths.Clear();
                SetStatus($"Showed all {_ymapList.Count} YMAP(s) in viewport.");
            }
            else
            {
                foreach (var path in _ymapList.Keys) _hiddenYmapPaths.Add(path);
                SetStatus($"Hidden all {_ymapList.Count} YMAP(s) from viewport.");
            }

            RefreshYmapList();
        }

        private async void YmapSendButton_Click(object sender, EventArgs e)
        {
            if (_ymapList.Count == 0) { SetStatus("YMAP list is empty."); return; }
            YmapSendButton.Enabled = false;
            SetStatus($"Sending {_ymapList.Count} YMAP(s)...");
            try
            {
                await SendJsonAsync(BuildYmapJson());
                SetStatus($"Sent {_ymapList.Count} YMAP(s) to Blender.");
            }
            catch (TaskCanceledException) { SetStatus("Timeout — is the server running in Blender?"); }
            catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
            finally { YmapSendButton.Enabled = true; }
        }

        private void YmapClearButton_Click(object sender, EventArgs e)
        {
            _ymapList.Clear();
            _ymapFileRefs.Clear();
            _hiddenYmapPaths.Clear();
            RefreshYmapList();
            SetStatus("YMAP list cleared.");
        }

        // ── YBN: collision selection helper ───────────────────────────────────

        /// <summary>
        /// Extracts the YBN filename from a Bounds object.
        /// Tries GetRootYbn().Name first (most reliable), then GetName() as fallback.
        /// </summary>
        private static string GetYbnName(Bounds b)
        {
            if (b == null) return null;
            // Prefer the root YbnFile name (always set during file load)
            var ybnName = b.GetRootYbn()?.Name;
            if (!string.IsNullOrEmpty(ybnName)) return ybnName;
            // Fallback: traverse parent chain for OwnerName
            var name = b.GetName();
            if (!string.IsNullOrEmpty(name)) return name;
            return null;
        }

        /// <summary>
        /// Returns the unique YBN names from the current viewport collision selection.
        /// Requires the WorldForm to be in Collision selection mode.
        /// Handles CollisionBounds, CollisionPoly.Owner, and multi-selection.
        /// </summary>
        private List<string> GetCollisionSelection()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var wf = ProjectForm?.WorldForm;
            if (wf == null) return result.ToList();

            var sel = wf.CurrentMapSelection;

            void AddFromItem(MapSelection item)
            {
                // Try CollisionBounds first, then CollisionPoly's owner
                var b = item.CollisionBounds ?? item.CollisionPoly?.Owner ?? item.CollisionVertex?.Owner;
                var name = GetYbnName(b);
                if (!string.IsNullOrEmpty(name)) result.Add(name);
            }

            if (sel.MultipleSelectionItems != null && sel.MultipleSelectionItems.Length > 0)
            {
                foreach (var item in sel.MultipleSelectionItems)
                    AddFromItem(item);
            }
            else
            {
                AddFromItem(sel);
            }

            return result.ToList();
        }

        // ── YBN: ListView refresh ─────────────────────────────────────────────

        private void RefreshYbnList()
        {
            YbnListView.Items.Clear();
            foreach (var name in _ybnList)
            {
                var lvi = new ListViewItem(name);
                lvi.Tag = name;
                YbnListView.Items.Add(lvi);
            }
            YbnCountLabel.Text = $"{_ybnList.Count} YBN(s)";
        }

        // ── YBN: JSON builder ─────────────────────────────────────────────────

        private string BuildYbnJson(IEnumerable<string> names)
        {
            var sb = new StringBuilder();
            sb.Append("{\"ybns\":[");
            bool first = true;
            foreach (var name in names)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append($"\"{EscapeJson(name)}\"");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ── YBN: button handlers ──────────────────────────────────────────────

        private void YbnAddToListButton_Click(object sender, EventArgs e)
        {
            var names = GetCollisionSelection();
            if (names.Count == 0) { SetStatus("No collision selected — switch to Collision mode in WorldForm first."); return; }

            int added = 0;
            foreach (var name in names)
                if (_ybnList.Add(name)) added++;

            RefreshYbnList();
            SetStatus($"Added {added} YBN(s) ({names.Count - added} already in list).");
        }

        private void YbnRemoveButton_Click(object sender, EventArgs e)
        {
            var toRemove = YbnListView.SelectedItems
                .Cast<ListViewItem>()
                .Select(lvi => lvi.Tag as string)
                .Where(n => n != null)
                .ToList();

            foreach (var name in toRemove) _ybnList.Remove(name);
            RefreshYbnList();
            SetStatus("Removed selected YBN(s).");
        }

        private void YbnClearListButton_Click(object sender, EventArgs e)
        {
            _ybnList.Clear();
            RefreshYbnList();
            SetStatus("YBN list cleared.");
        }

        private async void YbnSendListButton_Click(object sender, EventArgs e)
        {
            if (_ybnList.Count == 0) { SetStatus("YBN list is empty — use \"Add to List\" first."); return; }
            YbnSendListButton.Enabled = false;
            SetStatus($"Sending {_ybnList.Count} YBN(s)...");
            try
            {
                await SendJsonAsync(BuildYbnJson(_ybnList));
                SetStatus($"Sent {_ybnList.Count} YBN(s) to Blender.");
            }
            catch (TaskCanceledException) { SetStatus("Timeout — is the server running in Blender?"); }
            catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
            finally { YbnSendListButton.Enabled = true; }
        }

        private async void YbnSendSelectionButton_Click(object sender, EventArgs e)
        {
            var names = GetCollisionSelection();
            if (names.Count == 0) { SetStatus("No collision selected — switch to Collision mode in WorldForm first."); return; }

            YbnSendSelectionButton.Enabled = false;
            SetStatus($"Sending {names.Count} YBN(s)...");
            try
            {
                await SendJsonAsync(BuildYbnJson(names));
                SetStatus($"Sent {names.Count} YBN(s) to Blender.");
            }
            catch (TaskCanceledException) { SetStatus("Timeout — is the server running in Blender?"); }
            catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
            finally { YbnSendSelectionButton.Enabled = true; }
        }
    }
}
