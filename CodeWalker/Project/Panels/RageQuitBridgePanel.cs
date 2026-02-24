using CodeWalker.GameFiles;
using CodeWalker.World;
using System;
using System.Collections.Generic;
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

        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        public RageQuitBridgePanel(ProjectForm projectForm)
        {
            ProjectForm = projectForm;
            InitializeComponent();
        }

        // ── Selection helpers ─────────────────────────────────────────────────

        private List<YmapEntityDef> GetSelectedEntities()
        {
            var result = new List<YmapEntityDef>();
            var wf = ProjectForm?.WorldForm;
            if (wf == null) return result;

            var sel = wf.CurrentMapSelection;

            if (sel.MultipleSelectionItems != null && sel.MultipleSelectionItems.Length > 0)
            {
                foreach (var item in sel.MultipleSelectionItems)
                {
                    if (item.EntityDef != null)
                        result.Add(item.EntityDef);
                }
            }
            else if (sel.EntityDef != null)
            {
                result.Add(sel.EntityDef);
            }

            return result;
        }

        // ── UI event handlers ─────────────────────────────────────────────────

        private void RefreshButton_Click(object sender, EventArgs e)
        {
            RefreshEntityList();
        }

        private void RefreshEntityList()
        {
            var entities = GetSelectedEntities();

            // Deduplicate by name
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unique = new List<YmapEntityDef>();
            foreach (var ent in entities)
            {
                var name = ent.Name;
                if (!string.IsNullOrEmpty(name) && seen.Add(name))
                    unique.Add(ent);
            }

            EntitiesListView.Items.Clear();
            foreach (var ent in unique)
            {
                var pos = ent.Position;
                var item = new ListViewItem(ent.Name);
                item.SubItems.Add($"{pos.X:F2}, {pos.Y:F2}, {pos.Z:F2}");
                item.Tag = ent;
                EntitiesListView.Items.Add(item);
            }

            StatusLabel.Text = $"{unique.Count} entity/ies ready ({entities.Count - unique.Count} duplicates removed)";
        }

        private async void SendButton_Click(object sender, EventArgs e)
        {
            if (EntitiesListView.Items.Count == 0)
            {
                StatusLabel.Text = "No entities in list. Click Refresh first.";
                return;
            }

            SendButton.Enabled = false;
            StatusLabel.Text = "Sending...";

            try
            {
                bool withPositions = WithPositionsCheckBox.Checked;
                int port = (int)PortNumericUpDown.Value;
                string url = $"http://localhost:{port}";
                string json = BuildJson(withPositions);

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                    StatusLabel.Text = $"Sent {EntitiesListView.Items.Count} asset(s) to Blender.";
                else
                    StatusLabel.Text = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
            }
            catch (TaskCanceledException)
            {
                StatusLabel.Text = "Timeout — is the server running in Blender?";
            }
            catch (HttpRequestException ex)
            {
                StatusLabel.Text = $"Connection error: {ex.Message}";
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error: {ex.Message}";
            }
            finally
            {
                SendButton.Enabled = true;
            }
        }

        private string BuildJson(bool withPositions)
        {
            var sb = new StringBuilder();

            if (withPositions)
            {
                sb.Append("{\"entities\":[");
                bool first = true;
                foreach (ListViewItem lvi in EntitiesListView.Items)
                {
                    var ent = lvi.Tag as YmapEntityDef;
                    if (ent == null) continue;
                    if (!first) sb.Append(",");
                    first = false;

                    var pos = ent.Position;
                    var ori = ent.Orientation;
                    sb.Append("{");
                    sb.Append($"\"name\":\"{EscapeJson(ent.Name)}\",");
                    sb.Append($"\"position\":{{\"x\":{pos.X},\"y\":{pos.Y},\"z\":{pos.Z}}},");
                    sb.Append($"\"rotation\":{{\"x\":{ori.X},\"y\":{ori.Y},\"z\":{ori.Z},\"w\":{ori.W}}}");
                    sb.Append("}");
                }
                sb.Append("]}");
            }
            else
            {
                sb.Append("{\"assets\":[");
                bool first = true;
                foreach (ListViewItem lvi in EntitiesListView.Items)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append($"\"{EscapeJson(lvi.Text)}\"");
                }
                sb.Append("]}");
            }

            return sb.ToString();
        }

        private static string EscapeJson(string s)
        {
            return s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
        }

        private void ImportAsMeshCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            // Visual feedback only — the value is read by BuildJson indirectly:
            // the "import_as_mesh" flag is carried in the JSON and handled by
            // the ragequit-blender server-side setting rq_cw_import_as_mesh.
            // No additional action needed here.
        }
    }
}
